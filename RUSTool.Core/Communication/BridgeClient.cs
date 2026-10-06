using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RUSTool.Communication;

/// <summary>
/// BridgeClient — 前端通信客户端唯一公共入口（供 UI/VM 调用）。
///
/// 职责：
/// 1. 建立 /control（必连）、/state 与 /sensor（按需）三条 WebSocket 通道；
/// 2. 指令下发 + 回执匹配（reply 按 id，event 按 ack_id）；
/// 3. 状态流接收（只保留最新一帧）；
/// 4. 感知二进制帧解码（<see cref="SensorFrameCodec"/>，在 WS 线程上解完再抛出，覆盖式只保留最新一帧）；
/// 5. 断线时通知上层并将所有未决请求置为失败。
/// </summary>
public sealed class BridgeClient : IDisposable
{
    private readonly ConnectionManager _connection;
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<CommandResult>> _pending = new();
    private long _nextId;
    private BridgeProtocol.StateFrame? _latestState;
    private CancellationTokenSource? _stateCts;
    private int _stateLoopRunning;
    private volatile bool _stateStreamEnabled;
    private SensorPointCloudFrame? _latestSensorFrame;
    private CancellationTokenSource? _sensorCts;
    private int _sensorLoopRunning;
    private volatile bool _sensorStreamEnabled;
    private long _sensorDecodeFailures;
    private CancellationTokenSource? _meshCts;
    private int _meshLoopRunning;
    private volatile bool _meshStreamEnabled;
    private long _meshDecodeFailures;
    private SensorPointCloudFrame? _latestPcMap;
    private CancellationTokenSource? _pcmapCts;
    private int _pcmapLoopRunning;
    private volatile bool _pcmapStreamEnabled;
    private long _pcmapDecodeFailures;
    private volatile bool _disposed;

    public BridgeClient(string host = "127.0.0.1", ushort port = 8765)
    {
        _connection = new ConnectionManager(host, port);
        _connection.ControlMessageReceived += OnControlMessage;
        _connection.StateMessageReceived += OnStateMessage;
        _connection.SensorMessageReceived += OnSensorMessage;
        _connection.MeshMessageReceived += OnMeshMessage;
        _connection.PcMapMessageReceived += OnPcMapMessage;
        _connection.ControlConnected += () => ConnectionChanged?.Invoke(true);
        _connection.ControlDisconnected += OnControlDisconnected;
        _connection.StateDisconnected += OnStateDisconnected;
        _connection.SensorDisconnected += OnSensorDisconnected;
        _connection.MeshDisconnected += OnMeshDisconnected;
        _connection.PcMapDisconnected += OnPcMapDisconnected;
    }

    /// <summary>/control 是否在线</summary>
    public bool IsConnected => _connection.IsControlConnected;

    /// <summary>连接/断线通知</summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>异步事件（长任务完成通知，如 plan_done）</summary>
    public event Action<EventNotification>? EventReceived;

    /// <summary>状态流更新（/state 通道）</summary>
    public event Action<BridgeProtocol.StateFrame>? StateUpdated;

    /// <summary>
    /// 感知流更新（/sensor 通道）。回调发生在 WebSocket 线程上，且是**覆盖式**的：
    /// 处理慢了就丢帧（后端只发最新一帧），因此回调里只该做「记下引用」这种事，
    /// 解压 / 反量化已经在本类里做完了。
    /// </summary>
    public event Action<SensorPointCloudFrame>? SensorFrameReceived;

    /// <summary>增量网格帧（<c>/mesh</c> 通道）。回调在 WebSocket 线程触发。</summary>
    public event Action<MeshFrame>? MeshFrameReceived;

    /// <summary>面元点云图（<c>/pcmap</c> 通道；解码后与 <c>/sensor</c> 同一个形状）。回调在 WebSocket 线程触发。</summary>
    public event Action<SensorPointCloudFrame>? PcMapFrameReceived;

    /// <summary>最新一帧状态（只保留最新）</summary>
    public BridgeProtocol.StateFrame? LatestState => _latestState;

    /// <summary>最新一帧点云（只保留最新；未开流 / 未收到时为 null）</summary>
    public SensorPointCloudFrame? LatestSensorFrame => _latestSensorFrame;

    /// <summary>最新一帧面元点云图（只保留最新；未开流 / 未收到时为 null）</summary>
    public SensorPointCloudFrame? LatestPcMap => _latestPcMap;


    /// <summary>指令日志回调（message, isError），由组装层注入，例如接到全局日志服务。</summary>
    public Action<string, bool>? Logger { get; set; }

    /// <summary>连 /control（必连），断线自动重连。</summary>
    public Task ConnectAsync()
    {
        ThrowIfDisposed();
        return _connection.ConnectControlAsync(_cts.Token);
    }

    /// <summary>开启 /state 状态流（需要状态可视化时调用）。</summary>
    public void StartStateStream()
    {
        ThrowIfDisposed();
        if (_stateStreamEnabled)
            return;
        _stateStreamEnabled = true;
        _stateCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        StartStateLoop();
    }

    /// <summary>关闭 /state 状态流。</summary>
    public void StopStateStream()
    {
        _stateStreamEnabled = false;
        _stateCts?.Cancel();
        // 异步断开，避免在 UI 线程同步等待造成死锁
        _ = DisconnectStateAsyncSafely();
    }

    /// <summary>
    /// 开启 /sensor 感知流（需要点云 / 影像时调用）。
    ///
    /// <para>
    /// 帧由 WebSocket 线程解码后抛出（见 <see cref="SensorFrameReceived"/>）；**未开流时后端不会发数据**
    /// （协议 §1：未连接的通道收不到数据），所以没人看点云时不必付这份带宽与 CPU。
    /// </para>
    /// </summary>
    public void StartSensorStream()
    {
        ThrowIfDisposed();
        if (_sensorStreamEnabled)
            return;
        _sensorStreamEnabled = true;
        _sensorCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        StartSensorLoop();
    }

    /// <summary>关闭 /sensor 感知流。</summary>
    public void StopSensorStream()
    {
        _sensorStreamEnabled = false;
        _sensorCts?.Cancel();
        _latestSensorFrame = null;
        // 异步断开，避免在 UI 线程同步等待造成死锁
        _ = DisconnectSensorAsyncSafely();
    }

    /// <summary>开启 /mesh 增量网格流（可靠有序；默认关，需要时调用）。</summary>
    public void StartMeshStream()
    {
        ThrowIfDisposed();
        if (_meshStreamEnabled)
            return;
        _meshStreamEnabled = true;
        _meshCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        StartMeshLoop();
    }

    /// <summary>关闭 /mesh 增量网格流。</summary>
    public void StopMeshStream()
    {
        _meshStreamEnabled = false;
        _meshCts?.Cancel();
        _ = DisconnectMeshAsyncSafely();
    }

    /// <summary>开启 /pcmap 面元点云图流（覆盖式）。</summary>
    public void StartPcMapStream()
    {
        ThrowIfDisposed();
        if (_pcmapStreamEnabled)
            return;
        _pcmapStreamEnabled = true;
        _pcmapCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
        StartPcMapLoop();
    }

    /// <summary>关闭 /pcmap 面元点云图流。</summary>
    public void StopPcMapStream()
    {
        _pcmapStreamEnabled = false;
        _pcmapCts?.Cancel();
        _latestPcMap = null;
        _ = DisconnectPcMapAsyncSafely();
    }

    /// <summary>
    /// 下发指令并等待回执（阻塞式）。
    /// 超时（默认 5000ms）未收到 reply → Success=false, Message="timeout"。
    /// </summary>
    public async Task<CommandResult> SendAsync(string cmd, double[]? args = null,
        int timeoutMs = 5000, CancellationToken ct = default, string? text = null)
    {
        var argText = args is { Length: > 0 } ? string.Join(",", args) : "";
        var textSuffix = string.IsNullOrEmpty(text) ? "" : $" text=\"{text}\"";
        Logger?.Invoke($"→ 发送指令 {cmd} [{argText}]{textSuffix}", false);

        var result = await SendCoreAsync(cmd, args, timeoutMs, ct, text);

        string code = !result.Success && result.ErrorCode != 0 ? $" (code {result.ErrorCode})" : "";
        Logger?.Invoke($"← 指令 {cmd} 结果: {(result.Success ? "成功" : "失败")} - {result.Message}{code}",
            !result.Success);
        return result;
    }

    private async Task<CommandResult> SendCoreAsync(string cmd, double[]? args,
        int timeoutMs, CancellationToken ct, string? text = null)
    {
        ThrowIfDisposed();

        var id = unchecked((uint)Interlocked.Increment(ref _nextId));
        var json = BridgeProtocol.Encode(new BridgeProtocol.Command(id, cmd, args ?? [], text ?? ""));
        var tcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            await _connection.SendAsync(Encoding.UTF8.GetBytes(json), ct);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);
            return new CommandResult(false, ex.Message, [], []);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);
        try
        {
            return await tcs.Task.WaitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // 超时：从字典移除并返回失败
            _pending.TryRemove(id, out _);
            return new CommandResult(false, "timeout", [], []);
        }
        catch (OperationCanceledException)
        {
            // 调用方主动取消
            _pending.TryRemove(id, out _);
            throw;
        }
    }

    /// <summary>断开所有连接（客户端仍可再次 ConnectAsync）。</summary>
    public void Disconnect()
    {
        // 同时停掉两条按需流，否则它们会按断线重连逻辑自行恢复
        _stateStreamEnabled = false;
        _stateCts?.Cancel();
        _sensorStreamEnabled = false;
        _sensorCts?.Cancel();
        _latestSensorFrame = null;
        _meshStreamEnabled = false;
        _meshCts?.Cancel();
        _pcmapStreamEnabled = false;
        _pcmapCts?.Cancel();
        _latestPcMap = null;
        _connection.DisconnectAll();
        ConnectionChanged?.Invoke(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stateStreamEnabled = false;
        _sensorStreamEnabled = false;
        _cts.Cancel();
        _stateCts?.Cancel();
        _sensorCts?.Cancel();
        _meshCts?.Cancel();
        _pcmapCts?.Cancel();
        _connection.DisconnectAll();
        FailAllPending("client disposed");
        _stateCts?.Dispose();
        _sensorCts?.Dispose();
        _meshCts?.Dispose();
        _pcmapCts?.Dispose();
        _cts.Dispose();
    }

    // ────────────── 内部 ──────────────

    private void OnControlMessage(ReadOnlyMemory<byte> data)
    {
        var json = Encoding.UTF8.GetString(data.Span);
        var msg = BridgeProtocol.TryParseReply(json);
        if (msg is null)
            return;

        if (msg.Type == "reply")
        {
            if (_pending.TryRemove(msg.Id, out var tcs))
                tcs.TrySetResult(new CommandResult(msg.Success, msg.Message, msg.Result, msg.Strings ?? [], msg.ErrorCode));
        }
        else if (msg.Type == "event")
        {
            EventReceived?.Invoke(new EventNotification(msg.Event, msg.Success, msg.Message, msg.Result));
        }
    }

    private void OnStateMessage(ReadOnlyMemory<byte> data)
    {
        var json = Encoding.UTF8.GetString(data.Span);
        var frame = BridgeProtocol.TryParseState(json);
        if (frame is null)
            return;
        _latestState = frame;
        StateUpdated?.Invoke(frame);
    }

    /// <summary>
    /// /sensor 整帧到达：在这里（WebSocket 线程）解码，产出的纯数组直接交给上层。
    ///
    /// <para>
    /// 坏帧只记不抛 —— 覆盖式通道下坏帧可能每帧都来（例如后端换了帧类型），
    /// 所以只记第 1 次与之后每 100 次，不刷屏；解码失败的那一帧直接丢，等下一帧。
    /// </para>
    /// </summary>
    private void OnSensorMessage(ReadOnlyMemory<byte> data)
    {
        var frame = SensorFrameCodec.TryDecode(data, out string? error);
        if (frame is null)
        {
            long failures = Interlocked.Increment(ref _sensorDecodeFailures);
            if (failures == 1 || failures % 100 == 0)
                Logger?.Invoke($"感知帧解码失败（第 {failures} 次）：{error}", true);
            return;
        }

        _latestSensorFrame = frame;
        SensorFrameReceived?.Invoke(frame);
    }

    /// <summary>
    /// /mesh 整帧到达：在 WebSocket 线程解码成纯数据的 <see cref="MeshFrame"/>。
    /// 坏帧只记不抛（与 /sensor 同一约定，首帧 + 每 100 次记一条，不刷屏）。
    /// </summary>
    private void OnMeshMessage(ReadOnlyMemory<byte> data)
    {
        var frame = MeshFrameCodec.TryDecode(data, out string? error);
        if (frame is null)
        {
            long failures = Interlocked.Increment(ref _meshDecodeFailures);
            if (failures == 1 || failures % 100 == 0)
                Logger?.Invoke($"网格帧解码失败（第 {failures} 次）：{error}", true);
            return;
        }

        MeshFrameReceived?.Invoke(frame);
    }

    /// <summary>/pcmap 整帧到达：线格式同 /sensor，复用同一解码器（<c>scope=map</c>）。</summary>
    private void OnPcMapMessage(ReadOnlyMemory<byte> data)
    {
        var frame = SensorFrameCodec.TryDecode(data, out string? error);
        if (frame is null)
        {
            long failures = Interlocked.Increment(ref _pcmapDecodeFailures);
            if (failures == 1 || failures % 100 == 0)
                Logger?.Invoke($"面元点云帧解码失败（第 {failures} 次）：{error}", true);
            return;
        }

        _latestPcMap = frame;
        PcMapFrameReceived?.Invoke(frame);
    }

    private void OnControlDisconnected()
    {
        ConnectionChanged?.Invoke(false);
        FailAllPending("connection lost");
    }

    private void OnStateDisconnected()
    {
        // 状态流断线后自动重连（直到 StopStateStream / Dispose）
        if (_stateStreamEnabled && !_disposed)
            StartStateLoop();
    }

    private void OnSensorDisconnected()
    {
        // 感知流断线后自动重连（直到 StopSensorStream / Dispose）
        if (_sensorStreamEnabled && !_disposed)
            StartSensorLoop();
    }

    private void OnMeshDisconnected()
    {
        if (_meshStreamEnabled && !_disposed)
            StartMeshLoop();
    }

    private void OnPcMapDisconnected()
    {
        if (_pcmapStreamEnabled && !_disposed)
            StartPcMapLoop();
    }

    /// <summary>
    /// 启动状态流重连循环（同一时刻仅允许一个，防止断线风暴导致并发连接）。
    /// </summary>
    private void StartStateLoop()
    {
        if (Interlocked.Exchange(ref _stateLoopRunning, 1) != 0)
            return;
        _ = Task.Run(StateStreamLoopAsync, CancellationToken.None);
    }

    private async Task StateStreamLoopAsync()
    {
        try
        {
            var stateCts = _stateCts;
            while (_stateStreamEnabled && !_disposed && stateCts is not null)
            {
                try
                {
                    await _connection.ConnectStateAsync(stateCts.Token).ConfigureAwait(false);
                    return; // 连上后由 StateDisconnected 驱动下一次连接
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    // 连接失败，退避后重试
                    try
                    {
                        await Task.Delay(1000, stateCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _stateLoopRunning, 0);
        }
    }

    private async Task DisconnectStateAsyncSafely()
    {
        try
        {
            await _connection.DisconnectStateAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 断开失败无需处理，下次连接会覆盖
        }
    }

    /// <summary>启动感知流重连循环（与状态流同一套：同一时刻仅允许一个）。</summary>
    private void StartSensorLoop()
    {
        if (Interlocked.Exchange(ref _sensorLoopRunning, 1) != 0)
            return;
        _ = Task.Run(SensorStreamLoopAsync, CancellationToken.None);
    }

    private async Task SensorStreamLoopAsync()
    {
        try
        {
            var sensorCts = _sensorCts;
            while (_sensorStreamEnabled && !_disposed && sensorCts is not null)
            {
                try
                {
                    await _connection.ConnectSensorAsync(sensorCts.Token).ConfigureAwait(false);
                    return; // 连上后由 SensorDisconnected 驱动下一次连接
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception)
                {
                    // 连接失败，退避后重试
                    try
                    {
                        await Task.Delay(1000, sensorCts.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _sensorLoopRunning, 0);
        }
    }

    private async Task DisconnectSensorAsyncSafely()
    {
        try
        {
            await _connection.DisconnectSensorAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 断开失败无需处理，下次连接会覆盖
        }
    }

    // ── /mesh 与 /pcmap 的重连循环（与 /sensor 同一套） ──

    private void StartMeshLoop()
    {
        if (Interlocked.Exchange(ref _meshLoopRunning, 1) != 0)
            return;
        _ = Task.Run(MeshStreamLoopAsync, CancellationToken.None);
    }

    private async Task MeshStreamLoopAsync()
    {
        try
        {
            var cts = _meshCts;
            while (_meshStreamEnabled && !_disposed && cts is not null)
            {
                try
                {
                    await _connection.ConnectMeshAsync(cts.Token).ConfigureAwait(false);
                    return; // 连上后由 MeshDisconnected 驱动下一次连接
                }
                catch (OperationCanceledException) { return; }
                catch (Exception)
                {
                    try { await Task.Delay(1000, cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _meshLoopRunning, 0);
        }
    }

    private void StartPcMapLoop()
    {
        if (Interlocked.Exchange(ref _pcmapLoopRunning, 1) != 0)
            return;
        _ = Task.Run(PcMapStreamLoopAsync, CancellationToken.None);
    }

    private async Task PcMapStreamLoopAsync()
    {
        try
        {
            var cts = _pcmapCts;
            while (_pcmapStreamEnabled && !_disposed && cts is not null)
            {
                try
                {
                    await _connection.ConnectPcMapAsync(cts.Token).ConfigureAwait(false);
                    return;
                }
                catch (OperationCanceledException) { return; }
                catch (Exception)
                {
                    try { await Task.Delay(1000, cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _pcmapLoopRunning, 0);
        }
    }

    private async Task DisconnectMeshAsyncSafely()
    {
        try { await _connection.DisconnectMeshAsync().ConfigureAwait(false); }
        catch (Exception) { }
    }

    private async Task DisconnectPcMapAsyncSafely()
    {
        try { await _connection.DisconnectPcMapAsync().ConfigureAwait(false); }
        catch (Exception) { }
    }

    private void FailAllPending(string message)
    {
        foreach (var (id, tcs) in _pending)
        {
            if (_pending.TryRemove(id, out _))
                tcs.TrySetResult(new CommandResult(false, message, [], []));
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BridgeClient));
    }
}

/// <summary>
/// 指令回执（对上层友好的返回值封装）。<see cref="Strings"/> 是文本结果
/// （协议 v0.4）：recorder / replay 的「文件名清单 / 当前文件名」等都从这里取。
/// </summary>
public sealed record CommandResult(bool Success, string Message, double[] Result, string[] Strings, uint ErrorCode = 0);

/// <summary>
/// 异步事件通知（对上层友好的返回值封装）。<see cref="Result"/> 是事件携带的数值结果 ——
/// 目前用于 <c>plan_done</c> 的**规划轨迹**（扁平化的三维点序列 <c>[x,y,z, …]</c>，m，base_link）。
/// </summary>
public sealed record EventNotification(string EventName, bool Success, string Message, double[] Result);
