using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RUSTool.Models.Robot;

/// <summary>
/// WebSocket 机器人客户端 — 状态订阅 + 指令发送
/// </summary>
public class RobotClient : IAsyncDisposable
{
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _cts;
    private int _requestId;
    private TaskCompletionSource<object?> _disconnectedTcs = new();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<CommandResponse>> _pendingCommands = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    // 事件
    /// <summary>状态推送事件</summary>
    public event Action<RobotState>? OnStateUpdated;
    /// <summary>错误消息事件</summary>
    public event Action<string>? OnError;
    /// <summary>连接成功事件</summary>
    public event Action? OnConnected;
    /// <summary>连接断开事件</summary>
    public event Action? OnDisconnected;

    // 属性
    /// <summary>是否已连接</summary>
    public bool IsConnected => _ws?.State == WebSocketState.Open;

    // 连接 / 断开
    internal async Task ConnectAsync(string url = "ws://localhost:8765", CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(url), _cts.Token);
        OnConnected?.Invoke();
        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token), _cts.Token);
    }

    /// <summary>断开连接</summary>
    public async Task DisconnectAsync()
    {
        await _cts?.CancelAsync()!;
        if (_ws?.State == WebSocketState.Open)
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
        _ws?.Dispose();
        _ws = null;
    }

    // 发送指令
    /// <summary>发送指令并等待响应（10s 超时）</summary>
    public async Task<CommandResponse> SendCommandAsync(string cmd, double[]? args = null,
        CancellationToken ct = default)
    {
        var id = Interlocked.Increment(ref _requestId);
        var request = new CommandRequest { Cmd = cmd, Args = args ?? [], Id = id };
        var json = JsonSerializer.Serialize(request, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);

        var tcs = new TaskCompletionSource<CommandResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingCommands[id] = tcs;

        try
        {
            await _ws!.SendAsync(bytes, WebSocketMessageType.Text, true, ct);
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10), ct);
        }
        catch
        {
            _pendingCommands.TryRemove(id, out _);
            throw;
        }
    }

    // 接收循环
    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[65536];

        try
        {
            while (_ws?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var result = await _ws.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                if (result.MessageType != WebSocketMessageType.Text)
                    continue;

                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                HandleMessage(json);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            OnError?.Invoke(ex.Message);
        }
        finally
        {
            OnDisconnected?.Invoke();
            _disconnectedTcs.TrySetResult(null);
        }
    }

    private void HandleMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 有 success 字段 -> 指令响应
        if (root.TryGetProperty("success", out _))
        {
            var response = JsonSerializer.Deserialize<CommandResponse>(json, JsonOptions);
            if (response != null && _pendingCommands.TryRemove(response.Id, out var tcs))
                tcs.TrySetResult(response);
        }
        // 否则 -> 状态推送
        else
        {
            var state = JsonSerializer.Deserialize<RobotState>(json, JsonOptions);
            if (state != null)
                OnStateUpdated?.Invoke(state);
        }
    }

    // 自动重连
    /// <summary>持续运行，断线后 5s 自动重连</summary>
    public async Task RunWithReconnectAsync(string url = "ws://localhost:8765",
        CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                _disconnectedTcs = new TaskCompletionSource<object?>();
                await ConnectAsync(url, ct);
                // 等待断开
                await _disconnectedTcs.Task.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                OnError?.Invoke($"连接断开: {ex.Message}");
                await Task.Delay(5000, ct);
            }
        }
    }

    // 资源释放
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
