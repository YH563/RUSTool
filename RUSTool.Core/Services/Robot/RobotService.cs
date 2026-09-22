using System;
using System.Threading;
using System.Threading.Tasks;
using RUSTool.Communication;

namespace RUSTool.Services.Robot;

/// <summary>
/// 基于 BridgeClient 的机器人业务实现。协议字符串（Commands.*）只在这一层出现，
/// 上层通过类型化的业务方法调用，无需关心线协议细节。
/// </summary>
public sealed class RobotService : IRobotService
{
    private readonly BridgeClient _client;

    public RobotService(BridgeClient client)
    {
        _client = client;
        _client.ConnectionChanged += connected => ConnectionChanged?.Invoke(connected);
        _client.StateUpdated += frame => StateUpdated?.Invoke(frame);
        _client.SensorFrameReceived += frame => SensorFrameReceived?.Invoke(frame);
        _client.EventReceived += evt => EventReceived?.Invoke(evt);
    }

    public bool IsConnected => _client.IsConnected;

    public BridgeProtocol.StateFrame? LatestState => _client.LatestState;

    public SensorPointCloudFrame? LatestSensorFrame => _client.LatestSensorFrame;

    public event Action<bool>? ConnectionChanged;
    public event Action<BridgeProtocol.StateFrame>? StateUpdated;
    public event Action<SensorPointCloudFrame>? SensorFrameReceived;
    public event Action<EventNotification>? EventReceived;

    public Task ConnectAsync() => _client.ConnectAsync();

    public void Disconnect() => _client.Disconnect();

    public void StartStateStream() => _client.StartStateStream();

    public void StopStateStream() => _client.StopStateStream();

    public void StartSensorStream() => _client.StartSensorStream();

    public void StopSensorStream() => _client.StopSensorStream();

    public Task<CommandResult> SendAsync(string cmd, double[]? args = null,
        int timeoutMs = 5000, CancellationToken ct = default)
        => _client.SendAsync(cmd, args, timeoutMs, ct);

    public Task<CommandResult> MoveJAsync(double[] joints)
        => _client.SendAsync(Commands.MoveJ, joints);

    public Task<CommandResult> MoveLAsync(double[] pose)
        => _client.SendAsync(Commands.MoveL, pose);

    public Task<CommandResult> StartJogAsync(JogParameters jog)
        => _client.SendAsync(Commands.StartJog,
            [jog.RefFrame, jog.Axis, jog.Direction, jog.Speed, jog.Acceleration, jog.MaxDistance]);

    public Task<CommandResult> StopJogAsync()
        => _client.SendAsync(Commands.StopJogDecel);

    public Task<CommandResult> StopJogImmediateAsync()
        => _client.SendAsync(Commands.StopJogImmediate);

    public Task<CommandResult> SetMode(double mode)
        => _client.SendAsync(Commands.SetMode, [mode]);

    // ── 驱动控制 ──

    public Task<CommandResult> RobotEnableAsync(double enabled)
        => _client.SendAsync(Commands.RobotEnable, [enabled]);

    public Task<CommandResult> QueryIsConnectedAsync()
        => _client.SendAsync(Commands.IsConnected);

    public Task<CommandResult> GetStateAsync()
        => _client.SendAsync(Commands.GetState);

    public Task<CommandResult> SwitchDriverAsync(double driver)
        => _client.SendAsync(Commands.SwitchDriver, [driver]);

    // ── 扫查流程 ──

    public Task<CommandResult> PreScanStartAsync()
        => _client.SendAsync(Commands.PreScanStart);

    public Task<CommandResult> PreScanEndAsync()
        => _client.SendAsync(Commands.PreScanEnd);

    public Task<CommandResult> SetStartPoseAsync()
        => _client.SendAsync(Commands.SetStartPose);

    public Task<CommandResult> SetEndPoseAsync()
        => _client.SendAsync(Commands.SetEndPose);

    public Task<CommandResult> PlanAsync()
        => _client.SendAsync(Commands.Plan);

    public Task<CommandResult> ExecuteAsync()
        => _client.SendAsync(Commands.Execute);

    public Task<CommandResult> StopAsync()
        => _client.SendAsync(Commands.Stop);

    public Task<CommandResult> PauseAsync()
        => _client.SendAsync(Commands.Pause);

    public Task<CommandResult> ResumeAsync(double mode=1, double enable=1)
        => _client.SendAsync(Commands.Resume, [mode, enable]);

    public Task<CommandResult> ResetAsync(double mode = 1, double enable = 1)
        => _client.SendAsync(Commands.Reset, [mode, enable]);

    public Task<CommandResult> QueryPreScanDoneAsync()
        => _client.SendAsync(Commands.QueryPreScanDone);

    public Task<CommandResult> QueryMotionDoneAsync()
        => _client.SendAsync(Commands.QueryMotionDone);

    // ── 仿真控制（仅 Sim 驱动） ──

    public Task<CommandResult> SetTimeSpeedAsync(double speed)
        => _client.SendAsync(Commands.SetTimeSpeed, [speed]);

    public Task<CommandResult> GetTimeSpeedAsync()
        => _client.SendAsync(Commands.GetTimeSpeed);

    public Task<CommandResult> GetSimTimeAsync()
        => _client.SendAsync(Commands.GetSimTime);

    public Task<CommandResult> GetFrameRateAsync()
        => _client.SendAsync(Commands.GetFrameRate);

    public Task<CommandResult> StepOnceAsync()
        => _client.SendAsync(Commands.StepOnce);

    public void Dispose() => _client.Dispose();
}
