using RUSTool.Communication;
using RUSTool.Services.Logging;
using System;
using System.IO;
using System.Threading;

namespace RUSTool.Replay;

/// <summary>回放状态（与后端旧 `replay_status` 的编码语义一致，便于对照）。</summary>
public enum ReplayState
{
    /// <summary>未载入 / 已停止（游标回到起点）。</summary>
    Idle = 0,

    /// <summary>正在按时间轴派发。</summary>
    Playing = 1,

    /// <summary>暂停（进度冻结，可继续）。</summary>
    Paused = 2,

    /// <summary>播到末尾。</summary>
    Finished = 3,
}

/// <summary>
/// 本地回放引擎（纯逻辑，不引用界面）：读一个 <c>.rusrec</c>，按时间轴把两条通道派发出去。
///
/// <para>
/// <b>时间轴：</b>按记录的有效时间戳（消息时间戳，缺失时退回入队时刻）铺开、首条平移到 0；
/// 时间戳回退（<c>map_clear</c> / 通道交错）钳到前一条，保证倍速播放不倒流。
/// 用后台 <see cref="Timer"/> 按 <see cref="TickInterval"/> 推进虚拟游标，
/// 到点的记录依次 <see cref="ReadPayload"/>（CRC 校验）→ 解码 → 抛事件。
/// </para>
/// <para>
/// <b>事件在后台线程触发</b>：订阅方（UI）负责 marshal 回自己的线程。
/// <b>解码也在后台线程</b>（含点云 zstd 解压），符合"感知帧别在 UI 线程解压"的约定。
/// </para>
/// <para>
/// 失败一律"停止回放 + 抛 <see cref="Error"/> 事件"，绝不继续发坏数据（与旧后端 replayer 同一口径）。
/// </para>
/// </summary>
public sealed class LocalReplayPlayer : IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(30);

    private readonly ILogService _log;
    private readonly Timer _timer;
    private readonly object _sync = new();

    private RusRecFile? _file;
    private TimelineEntry[] _timeline = [];
    private int _nextIndex;
    private double _cursor;
    private DateTime _lastTickUtc;
    private bool _disposed;

    public LocalReplayPlayer(ILogService log)
    {
        _log = log;
        _timer = new Timer(_ => OnTick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>一帧机械臂状态（通道 0）到达。</summary>
    public event Action<BridgeProtocol.StateFrame>? StateFramePlayed;

    /// <summary>一帧点云（通道 1）到达。</summary>
    public event Action<SensorPointCloudFrame>? PointCloudPlayed;

    /// <summary>任一状态 / 进度发生变化（订阅方据此刷新界面）。在后台线程触发。</summary>
    public event Action? Changed;

    /// <summary>回放中断（读盘 / CRC / 解码失败）。参数是给日志的原因。</summary>
    public event Action<string>? Error;

    // ── 状态 ──
    public bool IsLoaded { get; private set; }
    public double Duration { get; private set; }
    public double Position { get; private set; }
    public double Speed { get; private set; } = 1.0;
    public ReplayState State { get; private set; } = ReplayState.Idle;
    public int RecordCount { get; private set; }
    public bool HasIndex { get; private set; }
    public string CurrentFile { get; private set; } = "";

    /// <summary>打开一个录音并建立时间轴。文件打不开 / 无记录会抛（由调用方决定怎么提示）。</summary>
    public void Load(string path)
    {
        RusRecFile file = RusRecFile.Open(path); // 失败抛给调用方
        lock (_sync)
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _file?.Dispose();
            _file = file;
            BuildTimeline(file);

            CurrentFile = Path.GetFileName(path);
            IsLoaded = true;
            RecordCount = file.Records.Count;
            HasIndex = file.HasIndex;
            Duration = file.DurationSeconds;
            State = ReplayState.Idle;
            _cursor = 0;
            Position = 0;
            _nextIndex = 0;
        }
        RaiseChanged();
    }

    /// <summary>开始 / 继续播放（finished 时从头开始）。</summary>
    public void Play()
    {
        lock (_sync)
        {
            if (!IsLoaded)
                return;

            if (State == ReplayState.Finished)
            {
                _cursor = 0;
                Position = 0;
                _nextIndex = 0;
            }

            State = ReplayState.Playing;
            _lastTickUtc = DateTime.UtcNow;
            _timer.Change(TickInterval, TickInterval);
        }
        RaiseChanged();
    }

    /// <summary>暂停（进度冻结）。</summary>
    public void Pause()
    {
        lock (_sync)
        {
            if (State != ReplayState.Playing)
                return;
            State = ReplayState.Paused;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
        RaiseChanged();
    }

    /// <summary>停止并复位到起点（文件保留，可直接再 Play）。</summary>
    public void Stop()
    {
        lock (_sync)
        {
            State = ReplayState.Idle;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _cursor = 0;
            Position = 0;
            _nextIndex = 0;
        }
        RaiseChanged();
    }

    /// <summary>跳到相对文件起点 <paramref name="seconds"/> 秒。</summary>
    public void SeekTo(double seconds)
    {
        lock (_sync)
        {
            if (!IsLoaded || _timeline.Length == 0)
                return;

            _cursor = Math.Clamp(seconds, 0, Duration);
            Position = _cursor;
            _nextIndex = LowerBound(_cursor);

            // 定位后补发"游标之前最近的一帧状态"，避免 HUD / 曲线停在旧值。
            DispatchLastStateBefore(_nextIndex);

            if (State == ReplayState.Playing)
                _lastTickUtc = DateTime.UtcNow;
        }
        RaiseChanged();
    }

    /// <summary>快退 / 快进 5 秒。</summary>
    public void Skip(double deltaSeconds)
        => SeekTo(Position + deltaSeconds);

    /// <summary>单步派发下一条记录（仅 Idle / Paused / Finished 可用）。</summary>
    public void Step()
    {
        lock (_sync)
        {
            if (!IsLoaded || State == ReplayState.Playing || _nextIndex >= _timeline.Length)
                return;

            Dispatch(_timeline[_nextIndex].Record);
            Position = _timeline[_nextIndex].Time;
            _cursor = Position;
            _nextIndex++;
        }
        RaiseChanged();
    }

    /// <summary>设置倍速（0.05 ~ 20，播放中即时生效、不跳时间）。</summary>
    public void SetSpeed(double speed)
    {
        lock (_sync)
        {
            Speed = Math.Clamp(speed, 0.05, 20.0);
        }
        RaiseChanged();
    }

    // ── 内部 ──

    private void BuildTimeline(RusRecFile file)
    {
        var records = file.Records;
        if (records.Count == 0)
        {
            _timeline = [];
            return;
        }

        long baseNs = records[0].EffectiveStampNs;
        var timeline = new TimelineEntry[records.Count];
        long prev = long.MinValue;
        for (int i = 0; i < records.Count; i++)
        {
            long ns = records[i].EffectiveStampNs;
            if (ns < prev)
                ns = prev; // 时间戳回退钳到前一条
            prev = ns;
            timeline[i] = new TimelineEntry(records[i], (ns - baseNs) / 1e9);
        }

        _timeline = timeline;
    }

    private void OnTick()
    {
        bool finished = false;
        lock (_sync)
        {
            if (_disposed || State != ReplayState.Playing || _timeline.Length == 0)
                return;

            DateTime now = DateTime.UtcNow;
            double delta = (now - _lastTickUtc).TotalSeconds;
            _lastTickUtc = now;
            if (delta > 0)
                _cursor += delta * Speed;

            while (_nextIndex < _timeline.Length && _timeline[_nextIndex].Time <= _cursor)
            {
                Dispatch(_timeline[_nextIndex].Record);
                _nextIndex++;
            }

            if (_nextIndex >= _timeline.Length)
            {
                _cursor = Duration;
                Position = Duration;
                State = ReplayState.Finished;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                finished = true;
            }
            else
            {
                Position = _cursor;
            }
        }

        if (finished)
            _log.Log("回放结束", LogLevel.Info, "replay");
        RaiseChanged();
    }

    private void Dispatch(RusRecRecord record)
    {
        if (_file is null)
            return;

        try
        {
            byte[] payload = _file.ReadPayload(record);

            if (record.ChannelId == RecPayloadDecoder.ChannelRobotState)
            {
                StateFramePlayed?.Invoke(RecPayloadDecoder.DecodeRobotState(payload));
            }
            else if (record.ChannelId == RecPayloadDecoder.ChannelSensorFrame)
            {
                var cloud = RecPayloadDecoder.TryDecodeSensorPointCloud(payload, out string? error);
                if (cloud is not null)
                    PointCloudPlayed?.Invoke(cloud);
                else
                    _log.Log($"回放：帧 #{record.Index} 解码失败（{error}）", LogLevel.Warn, "replay");
            }
        }
        catch (Exception ex)
        {
            // 坏帧 / CRC 失败：停止并上报，绝不继续发坏数据。
            State = ReplayState.Idle;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _log.Log($"回放中断：记录 #{record.Index} {ex.Message}", LogLevel.Error, "replay");
            Error?.Invoke(ex.Message);
        }
    }

    private void DispatchLastStateBefore(int index)
    {
        for (int i = index - 1; i >= 0; i--)
        {
            if (_timeline[i].Record.ChannelId == RecPayloadDecoder.ChannelRobotState)
            {
                Dispatch(_timeline[i].Record);
                return;
            }
        }
    }

    private int LowerBound(double time)
    {
        int lo = 0, hi = _timeline.Length;
        while (lo < hi)
        {
            int mid = (lo + hi) >> 1;
            if (_timeline[mid].Time < time)
                lo = mid + 1;
            else
                hi = mid;
        }
        return lo;
    }

    private void RaiseChanged() => Changed?.Invoke();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _timer.Dispose();
        _file?.Dispose();
    }

    private readonly record struct TimelineEntry(RusRecRecord Record, double Time);
}
