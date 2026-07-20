using RUSTool.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RUSTool.Communication;

/// <summary>
/// 传输层协议接口
///
/// 职责：定义与后端通信的最基本契约 — 连接、发送原始指令、接收状态。
/// 实现可以是 WebSocket、gRPC、共享内存等。
/// </summary>
public interface IProtocolClient : IAsyncDisposable
{
    bool IsConnected { get; }

    event Action<RobotState>? OnStateUpdated;
    event Action? OnConnected;
    event Action? OnDisconnected;
    event Action<string>? OnError;

    Task ConnectAsync(string url, CancellationToken ct = default);
    Task DisconnectAsync();
    Task<CommandResponse> SendCommandAsync(string cmd, double[]? args = null,
        CancellationToken ct = default);
    Task RunWithReconnectAsync(string url, CancellationToken ct = default);
}
