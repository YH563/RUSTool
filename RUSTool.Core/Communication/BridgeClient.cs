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
/// 1. 建立 /control（必连）与 /state 两条 WebSocket 通道；
/// 2. 指令下发 + 回执匹配（reply 按 id，event 按 ack_id）；
/// 3. 状态流接收（只保留最新一帧）；
/// 4. 断线时通知上层并将所有未决请求置为失败。
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
    private volatile bool _disposed;

    public BridgeClient(string host = "127.0.0.1", ushort port = 8765)
    {
        _connection = new ConnectionManager(host, port);
        _connection.ControlMessageReceived += OnControlMessage;
        _connection.StateMessageReceived += OnStateMessage;
        _connection.ControlConnected += () => ConnectionChanged?.Invoke(true);
        _connection.ControlDisconnected += OnControlDisconnected;
        _connection.StateDisconnected += OnStateDisconnected;
    }

    /// <summary>/control 是否在线</summary>
    public bool IsConnected => _connection.IsControlConnected;

    /// <summary>连接/断线通知</summary>
    public event Action<bool>? ConnectionChanged;

    /// <summary>异步事件（长任务完成通知，如 plan_done）</summary>
    public event Action<EventNotification>? EventReceived;

    /// <summary>状态流更新（/state 通道）</summary>
    public event Action<BridgeProtocol.StateFrame>? StateUpdated;

    /// <summary>最新一帧状态（只保留最新）</summary>
    public BridgeProtocol.StateFrame? LatestState => _latestState;

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
    /// 下发指令并等待回执（阻塞式）。
    /// 超时（默认 5000ms）未收到 reply → Success=false, Message="timeout"。
    /// </summary>
    public async Task<CommandResult> SendAsync(string cmd, double[]? args = null,
        int timeoutMs = 5000, CancellationToken ct = default)
    {
        var argText = args is { Length: > 0 } ? string.Join(",", args) : "";
        Logger?.Invoke($"→ 发送指令 {cmd} [{argText}]", false);

        var result = await SendCoreAsync(cmd, args, timeoutMs, ct);

        Logger?.Invoke($"← 指令 {cmd} 结果: {(result.Success ? "成功" : "失败")} - {result.Message}",
            !result.Success);
        return result;
    }

    private async Task<CommandResult> SendCoreAsync(string cmd, double[]? args,
        int timeoutMs, CancellationToken ct)
    {
        ThrowIfDisposed();

        var id = unchecked((uint)Interlocked.Increment(ref _nextId));
        var json = BridgeProtocol.Encode(new BridgeProtocol.Command(id, cmd, args ?? []));
        var tcs = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        try
        {
            await _connection.SendAsync(Encoding.UTF8.GetBytes(json), ct);
        }
        catch (Exception ex)
        {
            _pending.TryRemove(id, out _);
            return new CommandResult(false, ex.Message, []);
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
            return new CommandResult(false, "timeout", []);
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
        // 同时停掉状态流，否则 /state 会按断线重连逻辑自行恢复
        _stateStreamEnabled = false;
        _stateCts?.Cancel();
        _connection.DisconnectAll();
        ConnectionChanged?.Invoke(false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stateStreamEnabled = false;
        _cts.Cancel();
        _stateCts?.Cancel();
        _connection.DisconnectAll();
        FailAllPending("client disposed");
        _stateCts?.Dispose();
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
                tcs.TrySetResult(new CommandResult(msg.Success, msg.Message, msg.Result));
        }
        else if (msg.Type == "event")
        {
            EventReceived?.Invoke(new EventNotification(msg.Event, msg.Success, msg.Message));
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

    private void FailAllPending(string message)
    {
        foreach (var (id, tcs) in _pending)
        {
            if (_pending.TryRemove(id, out _))
                tcs.TrySetResult(new CommandResult(false, message, []));
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(BridgeClient));
    }
}

/// <summary>指令回执（对上层友好的返回值封装）</summary>
public sealed record CommandResult(bool Success, string Message, double[] Result);

/// <summary>异步事件通知（对上层友好的返回值封装）</summary>
public sealed record EventNotification(string EventName, bool Success, string Message);
