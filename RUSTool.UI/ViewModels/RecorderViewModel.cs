using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Communication;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using System;
using System.Threading.Tasks;

namespace RUSTool.UI.ViewModels;

/// <summary>
/// 录制控制（RECORDER 旁路模块）：开始 / 停止 / 状态。
///
/// <para>
/// <b>录制是全局的、与手动 / 自动模式无关</b>，所以放在共享工具栏上；它只决定"录不录"，
/// 不决定"录什么"（通道由后端启动参数决定）。界面上看到的初始状态由一次 <c>recorder_status</c>
/// 回读决定 —— 不假设、不猜。
/// </para>
/// <para>
/// <b>连接后不自动录制：</b>后端 <c>recorder_node</c> 默认 <c>autostart=true</c>（节点一起来就在录），
/// 这是后端的默认行为、前端改不了它的启动参数。所以连上后一旦发现"在录、但不是本会话手动开的"，
/// 前端就主动 <c>recorder_stop</c> 一次，让应用落在"未录制"，由用户显式开始 ——
/// 每次连接只处理一次，且绝不会去停"用户自己开的录制"。
/// </para>
/// <para>
/// <b>7 项状态</b>（result，顺序是协议的一部分）：state / records / payload_mib / file_mib /
/// dropped / throttled / files；<c>strings[0]</c> 是当前 / 最后文件名。
/// state：0 = stopped、1 = recording、2 = failed。
/// </para>
/// </summary>
public sealed partial class RecorderViewModel : ViewModelBase
{
    /// <summary>录制状态轮询周期（秒）：状态指示与计时用，1 Hz 足够，不刷屏。</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    private readonly IRobotService _robot;
    private readonly ILogService _log;
    private readonly DispatcherTimer? _timer;

    /// <summary>本次录制开始时刻（本地计时用）；未录制为 null。</summary>
    private DateTime? _recordingSince;

    /// <summary>本次会话里，录制是不是「用户点的开始」发起的。后端 autostart 起的录不算。</summary>
    private bool _recordingInitiatedHere;

    /// <summary>本次连接是否已经处理过「后端自动录制」——只停一次，不反复和后端较劲。</summary>
    private bool _autoStopHandled;

    public RecorderViewModel(IRobotService robot, ILogService log)
    {
        _robot = robot;
        _log = log;

        _robot.ConnectionChanged += connected => Dispatcher.UIThread.Post(() =>
        {
            IsConnected = connected;
            if (connected)
            {
                // 每次连接都重新评估一次"要不要停掉后端自动录制"。
                _autoStopHandled = false;
                _ = RefreshAsync();
            }
        });

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += (_, _) => _ = RefreshAsync();
        _timer.Start();
    }

    /// <summary>控制通道是否在线（离线时录制按钮不可用）。</summary>
    [ObservableProperty]
    private bool _isConnected;

    // ── 状态（来自 recorder_status 的 7 项）──

    [ObservableProperty] private int _state;          // 0/1/2
    [ObservableProperty] private double _records;     // 累计记录数（跨 start/stop）
    [ObservableProperty] private double _payloadMiB;  // 累计 payload 体积
    [ObservableProperty] private double _fileMiB;     // 当前 / 最后文件大小
    [ObservableProperty] private double _dropped;     // 异常丢弃
    [ObservableProperty] private double _throttled;   // 限流丢弃
    [ObservableProperty] private double _fileCount;   // 已创建文件数
    [ObservableProperty] private string _currentFile = "";

    [ObservableProperty] private string _elapsedText = "";

    partial void OnStateChanged(int value)
    {
        if (value == 1)
            _recordingSince ??= DateTime.Now;
        else
        {
            _recordingSince = null;
            ElapsedText = "";
        }

        RaiseDerived();
    }

    public bool IsStopped => State == 0;
    public bool IsRecording => State == 1;
    public bool IsFailed => State == 2;

    /// <summary>可开始：在线、未在录、且未熔断（熔断必须重启节点）。</summary>
    public bool CanStart => IsConnected && State == 0;

    /// <summary>可停止：正在录制。</summary>
    public bool CanStop => State == 1;

    /// <summary>开关按钮是否可用（在线且未熔断）。</summary>
    public bool CanToggle => CanStart || CanStop;

    /// <summary>一个按钮的文案随状态切换（比两个互斥按钮省地方）。</summary>
    public string ActionText => IsRecording ? "停止录制" : "开始录制";

    /// <summary>状态文案：录制中带上本地计时。</summary>
    public string StatusText => State switch
    {
        1 => string.IsNullOrEmpty(ElapsedText) ? "录制中" : $"录制中 {ElapsedText}",
        2 => "录制失败",
        _ => "未录制",
    };

    /// <summary>详情（ToolTip / 状态行）：记录数、体积、丢弃、文件名。</summary>
    public string DetailText =>
        $"{Records:0} 条 · {PayloadMiB:0.0} MiB · 丢 {Dropped:0}/限 {Throttled:0} · 文件 {FileCount:0}" +
        (string.IsNullOrEmpty(CurrentFile) ? "" : $"\n{CurrentFile}");

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsRecording));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(CanStop));
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(DetailText));
    }

    partial void OnElapsedTextChanged(string value) => OnPropertyChanged(nameof(StatusText));

    /// <summary>开关录制：正在录就停，否则开始。未连接 / 熔断时不做任何事。</summary>
    [RelayCommand]
    private async Task Toggle()
    {
        if (IsRecording)
            await StopInternalAsync();
        else if (CanStart)
            await StartInternalAsync();
        else if (IsFailed)
            _log.Log("录制已熔断（写失败），需检查磁盘后重启录制节点", LogLevel.Warn, "recorder");
        else
            _log.Log("未连接控制通道，无法录制", LogLevel.Warn, "recorder");
    }

    private async Task StartInternalAsync()
    {
        var r = await _robot.RecorderStartAsync();
        Apply(r);
        if (r.Success)
        {
            _recordingInitiatedHere = true; // 用户点的开始：本次会话不再自动停
            _log.Log($"开始录制：{CurrentFile}", LogLevel.Success, "recorder");
        }
        else
        {
            _log.Log($"开始录制失败：{r.Message}", LogLevel.Error, "recorder");
        }
    }

    private async Task StopInternalAsync()
    {
        var r = await _robot.RecorderStopAsync();
        Apply(r);
        if (r.Success)
        {
            _recordingInitiatedHere = false;
            _log.Log($"停止录制，已封存：{CurrentFile}", LogLevel.Success, "recorder");
        }
        else
        {
            _log.Log($"停止录制失败：{r.Message}", LogLevel.Error, "recorder");
        }
    }

    private async Task RefreshAsync()
    {
        if (!IsConnected)
            return;

        var r = await _robot.RecorderStatusAsync();
        if (r.Success)
            Apply(r);

        // 连接后不自动录制：后端 autostart=true 会让节点一起来就在录。
        // 连上后一旦发现"在录、但不是本会话手动开的"，就主动停一次（只处理一次/连接）。
        if (State == 1 && !_recordingInitiatedHere && !_autoStopHandled)
        {
            _autoStopHandled = true;
            await StopInternalAsync();
            _log.Log("检测到后端自动录制，已停止；需要录制请手动点「开始录制」", LogLevel.Warn, "recorder");
        }
    }

    /// <summary>把回执的 7 项状态 + 文件名落到界面。结果字段不足时保持旧值。</summary>
    private void Apply(CommandResult r)
    {
        if (r.Result.Length >= 7)
        {
            State = (int)r.Result[0];
            Records = r.Result[1];
            PayloadMiB = r.Result[2];
            FileMiB = r.Result[3];
            Dropped = r.Result[4];
            Throttled = r.Result[5];
            FileCount = r.Result[6];
        }

        if (r.Strings.Length > 0 && !string.IsNullOrEmpty(r.Strings[0]))
            CurrentFile = r.Strings[0];

        // 录制中每帧刷新一下本地计时（轮询周期 1s，文本按秒跳）。
        if (IsRecording && _recordingSince is { } since)
            ElapsedText = Format(since, DateTime.Now);

        RaiseDerived();
    }

    private static string Format(DateTime from, DateTime to)
    {
        var span = to - from;
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        return $"{(int)span.TotalHours:00}:{span.Minutes:00}:{span.Seconds:00}";
    }
}
