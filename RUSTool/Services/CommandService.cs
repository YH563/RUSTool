using RUSTool.Models;
using RUSTool.Communication;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RUSTool.Services;

/// <summary>
/// 命令服务 — 业务层指令编排
///
/// 实现 ICommandService 接口，屏蔽底层协议细节。
/// 内部使用 IProtocolClient 发送指令，当前是 WebSocket，
/// 后续换成 gRPC/ROS 等只需替换构造时的 client 实现。
/// </summary>
public class CommandService : ICommandService
{
    private readonly IProtocolClient _client;

    // ── 事件 ──
    public event Action<RobotState>? OnStateUpdated;
    public event Action? OnConnected;
    public event Action? OnDisconnected;
    public event Action<string>? OnError;

    public bool IsConnected => _client.IsConnected;

    /// <summary>注入协议客户端（默认 WebSocket）</summary>
    public CommandService(IProtocolClient? client = null)
    {
        _client = client ?? new WebSocketClient();
        _client.OnStateUpdated += state => OnStateUpdated?.Invoke(state);
        _client.OnConnected     += ()     => OnConnected?.Invoke();
        _client.OnDisconnected  += ()     => OnDisconnected?.Invoke();
        _client.OnError         += msg   => OnError?.Invoke(msg);
    }

    // ── 连接管理 ──
    public Task ConnectAsync(string url, CancellationToken ct = default)
        => _client.ConnectAsync(url, ct);
    public Task DisconnectAsync()
        => _client.DisconnectAsync();

    // ── 运动控制指令 ──
    public Task SendMoveJointAsync(double[] joints, CancellationToken ct = default)
        => _client.SendCommandAsync("movej", joints, ct);
    public Task SendMoveLinearAsync(double[] pose, CancellationToken ct = default)
        => _client.SendCommandAsync("movel", pose, ct);
    public Task SendJogStartAsync(int refFrame, int axis, int direction,
        int speed, int acc, double maxDis, CancellationToken ct = default)
        => _client.SendCommandAsync("start_jog",
            [refFrame, axis, direction, speed, acc, maxDis], ct);
    public Task SendJogStopAsync(CancellationToken ct = default)
        => _client.SendCommandAsync("stop_jog_decel", [], ct);
    public Task SendJogStopImmediateAsync(CancellationToken ct = default)
        => _client.SendCommandAsync("stop_jog_immediate", [], ct);

    // ── 资源释放 ──
    public async ValueTask DisposeAsync()
        => await _client.DisposeAsync();
}
