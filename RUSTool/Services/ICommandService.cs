using RUSTool.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace RUSTool.Services;

/// <summary>
/// 命令服务接口 — 供 ViewModel 调用的业务层契约
///
/// 屏蔽底层协议细节，ViewModel 只关心"发什么指令"，
/// 不关心"怎么发"。
/// </summary>
public interface ICommandService : IAsyncDisposable
{
    bool IsConnected { get; }

    event Action<RobotState>? OnStateUpdated;
    event Action? OnConnected;
    event Action? OnDisconnected;
    event Action<string>? OnError;

    Task ConnectAsync(string url, CancellationToken ct = default);
    Task DisconnectAsync();

    Task SendMoveJointAsync(double[] joints, CancellationToken ct = default);
    Task SendMoveLinearAsync(double[] pose, CancellationToken ct = default);
    Task SendJogStartAsync(int refFrame, int axis, int direction,
        int speed, int acc, double maxDis, CancellationToken ct = default);
    Task SendJogStopAsync(CancellationToken ct = default);
    Task SendJogStopImmediateAsync(CancellationToken ct = default);
}
