using System;
using System.Buffers;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RUSTool.Communication;

/// <summary>
/// 连接管理：/control + /state + /sensor 三条通道，各一个 ClientWebSocket + 一个接收循环。
/// /control 断线后自动重连（500ms → 1s → 2s → 5s → 10s 封顶，连上即重置）；
/// /state 与 /sensor 断线后由 BridgeClient 的 Start*Stream 再次触发连接。
/// </summary>
internal sealed class ConnectionManager : IDisposable
{
    private static readonly int[] ReconnectDelaysMs = [500, 1000, 2000, 5000, 10000];

    private readonly string _host;
    private readonly ushort _port;

    private ClientWebSocket? _controlWs;
    private ClientWebSocket? _stateWs;
    private ClientWebSocket? _sensorWs;
    private CancellationTokenSource? _controlCts;
    private Task? _controlLoopTask;
    private bool _controlRequested;
    private TaskCompletionSource<bool>? _firstConnectTcs;

    public bool IsControlConnected => _controlWs?.State == WebSocketState.Open;

    /// <summary>/control 通道收到整帧文本/二进制（原样字节）</summary>
    public event Action<ReadOnlyMemory<byte>>? ControlMessageReceived;
    /// <summary>/state 通道收到整帧文本/二进制（原样字节）</summary>
    public event Action<ReadOnlyMemory<byte>>? StateMessageReceived;
    /// <summary>/sensor 通道收到整帧二进制（原样字节，未解码）</summary>
    public event Action<ReadOnlyMemory<byte>>? SensorMessageReceived;
    /// <summary>/control 连上通知（含重连成功后）</summary>
    public event Action? ControlConnected;
    /// <summary>/control 断线通知（重连由本类内部负责）</summary>
    public event Action? ControlDisconnected;
    /// <summary>/state 断线通知（重连由 BridgeClient 负责）</summary>
    public event Action? StateDisconnected;
    /// <summary>/sensor 断线通知（重连由 BridgeClient 负责）</summary>
    public event Action? SensorDisconnected;

    public ConnectionManager(string host, ushort port)
    {
        _host = host;
        _port = port;
    }

    /// <summary>建立 /control 连接并启动自动重连循环。首次连上后返回，已在运行则立即返回。</summary>
    public async Task ConnectControlAsync(CancellationToken ct)
    {
        TaskCompletionSource<bool> first;
        lock (this)
        {
            if (_controlRequested && _firstConnectTcs is not null)
            {
                first = _firstConnectTcs;
            }
            else
            {
                _controlRequested = true;
                _controlCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                first = _firstConnectTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                _controlLoopTask = Task.Run(() => ControlLoopAsync(_controlCts.Token), CancellationToken.None);
            }
        }
        await first.Task.WaitAsync(ct);
    }

    /// <summary>建立 /state 连接（单次尝试，不做自动重连）。</summary>
    public async Task ConnectStateAsync(CancellationToken ct)
    {
        // 若已有旧连接则先断开，避免叠加
        await DisconnectStateAsync();
        var ws = await ConnectAsync(Channels.State, ct);
        if (ct.IsCancellationRequested)
        {
            await CloseAsync(ws);
            throw new OperationCanceledException(ct);
        }
        _stateWs = ws;
        _ = Task.Run(() => StateReceiveLoopAsync(ws, ct), CancellationToken.None);
    }

    /// <summary>建立 /sensor 连接（单次尝试，不做自动重连）。</summary>
    public async Task ConnectSensorAsync(CancellationToken ct)
    {
        // 若已有旧连接则先断开，避免叠加
        await DisconnectSensorAsync();
        var ws = await ConnectAsync(Channels.Sensor, ct);
        if (ct.IsCancellationRequested)
        {
            await CloseAsync(ws);
            throw new OperationCanceledException(ct);
        }
        _sensorWs = ws;
        _ = Task.Run(() => SensorReceiveLoopAsync(ws, ct), CancellationToken.None);
    }

    /// <summary>沿 /control 通道发送整帧文本。</summary>
    public async Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        var ws = _controlWs;
        if (ws is null || ws.State != WebSocketState.Open)
            throw new InvalidOperationException("控制通道未连接");
        await ws.SendAsync(data, WebSocketMessageType.Text, true, ct);
    }

    /// <summary>断开 /state 通道。</summary>
    public async Task DisconnectStateAsync()
    {
        var ws = _stateWs;
        if (ws is null)
            return;
        _stateWs = null;
        await CloseAsync(ws);
    }

    /// <summary>断开 /sensor 通道。</summary>
    public async Task DisconnectSensorAsync()
    {
        var ws = _sensorWs;
        if (ws is null)
            return;
        _sensorWs = null;
        await CloseAsync(ws);
    }

    /// <summary>断开所有通道，停止重连循环。</summary>
    public void DisconnectAll()
    {
        lock (this)
        {
            _controlRequested = false;
            _controlCts?.Cancel();
            _firstConnectTcs?.TrySetCanceled();
            _firstConnectTcs = null;
        }
        _controlWs?.Dispose();
        _controlWs = null;
        _stateWs?.Dispose();
        _stateWs = null;
        _sensorWs?.Dispose();
        _sensorWs = null;
    }

    // ────────────── 内部 ──────────────

    private async Task<ClientWebSocket> ConnectAsync(string channel, CancellationToken ct)
    {
        var ws = new ClientWebSocket();
        try
        {
            await ws.ConnectAsync(new Uri($"ws://{_host}:{_port}{channel}"), ct);
            return ws;
        }
        catch
        {
            ws.Dispose();
            throw;
        }
    }

    private async Task ControlLoopAsync(CancellationToken ct)
    {
        var attempt = 0;
        var wasConnected = false;
        while (!ct.IsCancellationRequested)
        {
            ClientWebSocket? ws = null;
            try
            {
                ws = await ConnectAsync(Channels.Control, ct);
                _controlWs = ws;
                attempt = 0; // 连上即重置退避
                wasConnected = true;
                _firstConnectTcs?.TrySetResult(true);
                ControlConnected?.Invoke();
                await ControlReceiveLoopAsync(ws, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException)
            {
                // 连接失败或断开 → 退避重试
            }
            catch (Exception)
            {
                // 非预期的错误同样走退避重试
            }
            finally
            {
                // 无论 socket 处于何种状态都释放，避免取消/异常路径泄漏连接
                if (ws is not null)
                {
                    if (ReferenceEquals(_controlWs, ws))
                        _controlWs = null;
                    ws.Dispose();
                }
            }

            if (wasConnected)
            {
                wasConnected = false;
                ControlDisconnected?.Invoke();
            }

            if (ct.IsCancellationRequested)
                break;

            var delay = ReconnectDelaysMs[Math.Min(attempt, ReconnectDelaysMs.Length - 1)];
            attempt = Math.Min(attempt + 1, ReconnectDelaysMs.Length - 1);
            try
            {
                await Task.Delay(delay, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task ControlReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var frame = await ReceiveFrameAsync(ws, ct);
            if (frame is null)
                break;
            ControlMessageReceived?.Invoke(frame.Value);
        }
    }

    private async Task StateReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var frame = await ReceiveFrameAsync(ws, ct);
                if (frame is null)
                    break;
                StateMessageReceived?.Invoke(frame.Value);
            }
        }
        catch (OperationCanceledException)
        {
            // 连接被取消，正常退出
        }
        catch (Exception)
        {
            // 断开，交给 BridgeClient 重连
        }
        finally
        {
            StateDisconnected?.Invoke();
            ws.Dispose();
        }
    }

    /// <summary>
    /// /sensor 接收循环：与 /state 同构，但帧是二进制的、且可能几 MB ——
    /// <see cref="ReceiveFrameAsync"/> 已经把分片拼回一条完整消息（绝不能按「收到一次数据 = 一帧」处理）。
    /// </summary>
    private async Task SensorReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        try
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var frame = await ReceiveFrameAsync(ws, ct);
                if (frame is null)
                    break;
                SensorMessageReceived?.Invoke(frame.Value);
            }
        }
        catch (OperationCanceledException)
        {
            // 连接被取消，正常退出
        }
        catch (Exception)
        {
            // 断开，交给 BridgeClient 重连
        }
        finally
        {
            SensorDisconnected?.Invoke();
            ws.Dispose();
        }
    }

    /// <summary>整帧接收一条消息（文本/二进制），返回原始字节；连接关闭返回 null。</summary>
    private static async Task<ReadOnlyMemory<byte>?> ReceiveFrameAsync(ClientWebSocket ws, CancellationToken ct)
    {
        // 高频状态帧下避免每帧分配 64KB，改用池化 buffer
        var buffer = ArrayPool<byte>.Shared.Rent(65536);
        try
        {
            using var ms = new MemoryStream();
            while (true)
            {
                var result = await ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    return null;
                if (result.MessageType is not (WebSocketMessageType.Text or WebSocketMessageType.Binary))
                    continue;

                ms.Write(buffer, 0, result.Count);
                if (result.EndOfMessage)
                    return ms.ToArray();
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static async Task CloseAsync(ClientWebSocket ws)
    {
        try
        {
            if (ws.State == WebSocketState.Open)
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        }
        catch (Exception)
        {
            // 忽略关闭异常
        }
        finally
        {
            ws.Dispose();
        }
    }

    public void Dispose()
    {
        DisconnectAll();
    }
}
