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
        _client.MeshFrameReceived += frame => MeshFrameReceived?.Invoke(frame);
        _client.PcMapFrameReceived += frame => PcMapFrameReceived?.Invoke(frame);
        _client.EventReceived += evt => EventReceived?.Invoke(evt);
    }

    public bool IsConnected => _client.IsConnected;

    public BridgeProtocol.StateFrame? LatestState => _client.LatestState;

    public SensorPointCloudFrame? LatestSensorFrame => _client.LatestSensorFrame;

    public SensorPointCloudFrame? LatestPcMap => _client.LatestPcMap;

    public event Action<bool>? ConnectionChanged;
    public event Action<BridgeProtocol.StateFrame>? StateUpdated;
    public event Action<SensorPointCloudFrame>? SensorFrameReceived;
    public event Action<MeshFrame>? MeshFrameReceived;
    public event Action<SensorPointCloudFrame>? PcMapFrameReceived;
    public event Action<EventNotification>? EventReceived;

    public Task ConnectAsync() => _client.ConnectAsync();

    public void Disconnect() => _client.Disconnect();

    public void StartStateStream() => _client.StartStateStream();

    public void StopStateStream() => _client.StopStateStream();

    public void StartSensorStream() => _client.StartSensorStream();

    public void StopSensorStream() => _client.StopSensorStream();

    public void StartMeshStream() => _client.StartMeshStream();

    public void StopMeshStream() => _client.StopMeshStream();

    public void StartPcMapStream() => _client.StartPcMapStream();

    public void StopPcMapStream() => _client.StopPcMapStream();

    public Task<CommandResult> SendAsync(string cmd, double[]? args = null,
        int timeoutMs = 5000, CancellationToken ct = default, string? text = null)
        => _client.SendAsync(cmd, args, timeoutMs, ct, text);

    public Task<CommandResult> MoveJAsync(double[] joints, double? speed = null, double? acc = null)
        => _client.SendAsync(Commands.MoveJ, WithMotionParams(joints, speed, acc));

    public Task<CommandResult> MoveLAsync(double[] pose, double? speed = null, double? acc = null)
        => _client.SendAsync(Commands.MoveL, WithMotionParams(pose, speed, acc));

    /// <summary>
    /// 把可选的速度 / 加速度拼到 movej / movel 参数的尾部（协议里它们是可选的后两个参数，
    /// 且是**比例 0~1**）。
    ///
    /// <para>
    /// 只给 <paramref name="speed"/> 是合法的；<paramref name="acc"/> 只有在 speed 也存在时才拼 ——
    /// 否则 acc 会错位到 speed 的位置，被后端当成速度解释。
    /// </para>
    /// </summary>
    private static double[] WithMotionParams(double[] values, double? speed, double? acc)
    {
        if (speed is null)
            return values;

        var result = new double[values.Length + (acc is null ? 1 : 2)];
        Array.Copy(values, result, values.Length);
        result[values.Length] = speed.Value;
        if (acc is not null)
            result[values.Length + 1] = acc.Value;

        return result;
    }

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

    public Task<CommandResult> SwitchDriverAsync(RobotDriver driver)
        => _client.SendAsync(Commands.SwitchDriver, [RobotDriverCodec.ToProtocol(driver)]);

    public Task<CommandResult> QueryDriverTypeAsync()
        => _client.SendAsync(Commands.GetDriverType);

    // ── 扫查流程 ──

    public Task<CommandResult> PreScanStartAsync()
        => _client.SendAsync(Commands.PreScanStart);

    public Task<CommandResult> PreScanEndAsync()
        => _client.SendAsync(Commands.PreScanEnd);

    public Task<CommandResult> PreScanDoneAsync()
        => _client.SendAsync(Commands.PreScanDone);

    public Task<CommandResult> SetStartPoseAsync(double[]? pose = null)
        => _client.SendAsync(Commands.SetStartPose, pose ?? []);

    public Task<CommandResult> SetEndPoseAsync(double[]? pose = null)
        => _client.SendAsync(Commands.SetEndPose, pose ?? []);

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

    // ── 录制（RECORDER 旁路） ──

    public Task<CommandResult> RecorderStartAsync()
        => _client.SendAsync(Commands.RecorderStart);

    public Task<CommandResult> RecorderStopAsync()
        => _client.SendAsync(Commands.RecorderStop);

    public Task<CommandResult> RecorderStatusAsync()
        => _client.SendAsync(Commands.RecorderStatus);

    // ── 回放（❌ 后端已废弃：过渡期保留，实现只做协议转发） ──
    // 这里引用的是标记 [Obsolete] 的协议常量，故本段整体抑制 CS0618。

#pragma warning disable CS0618

    [Obsolete(ReplayDeprecation.Message)]
    public Task<CommandResult> ReplayListAsync()
        => _client.SendAsync(Commands.ReplayList);

    [Obsolete(ReplayDeprecation.Message)]
    public Task<CommandResult> ReplayLoadAsync(int? index = null)
        => _client.SendAsync(Commands.ReplayLoad, index is null ? [] : [index.Value]);

    [Obsolete(ReplayDeprecation.Message)]
    public Task<CommandResult> ReplayLoadPathAsync(string path)
        => _client.SendAsync(Commands.ReplayLoadPath, text: path);

    [Obsolete(ReplayDeprecation.Message)]
    public Task<CommandResult> ReplayStartAsync(double? speed = null)
        => _client.SendAsync(Commands.ReplayStart, speed is null ? [] : [speed.Value]);

    [Obsolete(ReplayDeprecation.Message)]
    public Task<CommandResult> ReplayPauseAsync()
        => _client.SendAsync(Commands.ReplayPause);

    [Obsolete(ReplayDeprecation.Message)]
    public Task<CommandResult> ReplayResumeAsync()
        => _client.SendAsync(Commands.ReplayResume);

    [Obsolete(ReplayDeprecation.Message)]
    public Task<CommandResult> ReplayStopAsync()
        => _client.SendAsync(Commands.ReplayStop);

    [Obsolete(ReplayDeprecation.Message)]
    public Task<CommandResult> ReplaySeekAsync(double seconds)
        => _client.SendAsync(Commands.ReplaySeek, [seconds]);

    [Obsolete(ReplayDeprecation.Message)]
    public Task<CommandResult> ReplaySetSpeedAsync(double speed)
        => _client.SendAsync(Commands.ReplaySetSpeed, [speed]);

    [Obsolete(ReplayDeprecation.Message)]
    public Task<CommandResult> ReplayStepAsync(int count = 1)
        => _client.SendAsync(Commands.ReplayStep, [count]);

    [Obsolete(ReplayDeprecation.Message)]
    public Task<CommandResult> ReplayStatusAsync()
        => _client.SendAsync(Commands.ReplayStatus);

#pragma warning restore CS0618

    public void Dispose() => _client.Dispose();
}
