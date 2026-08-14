using System;
using System.Threading;
using System.Threading.Tasks;
using RUSTool.Communication;

namespace RUSTool.Services.Robot;

/// <summary>
/// 机器人业务服务接口。上层（ViewModel）只依赖此接口，
/// 不直接接触 BridgeClient、WebSocket 通道或协议字符串。
/// </summary>
public interface IRobotService : IDisposable
{
    /// <summary>/control 通道是否在线</summary>
    bool IsConnected { get; }

    /// <summary>最新一帧状态（只保留最新）</summary>
    BridgeProtocol.StateFrame? LatestState { get; }

    /// <summary>连接 / 断线通知</summary>
    event Action<bool>? ConnectionChanged;

    /// <summary>状态流更新（/state 通道，后台线程触发）</summary>
    event Action<BridgeProtocol.StateFrame>? StateUpdated;

    /// <summary>异步事件（长任务完成通知，如 plan_done）</summary>
    event Action<EventNotification>? EventReceived;

    /// <summary>连 /control（断线自动重连）。</summary>
    Task ConnectAsync();

    /// <summary>断开所有连接。</summary>
    void Disconnect();

    /// <summary>开启 /state 状态流。</summary>
    void StartStateStream();

    /// <summary>关闭 /state 状态流。</summary>
    void StopStateStream();

    /// <summary>下发任意指令（扩展用）。</summary>
    Task<CommandResult> SendAsync(string cmd, double[]? args = null,
        int timeoutMs = 5000, CancellationToken ct = default);

    /// <summary>关节空间运动（q1..q6）。</summary>
    Task<CommandResult> MoveJAsync(double[] joints);

    /// <summary>笛卡尔直线运动（x,y,z,rx,ry,rz）。</summary>
    Task<CommandResult> MoveLAsync(double[] pose);

    /// <summary>启动点动。</summary>
    Task<CommandResult> StartJogAsync(JogParameters jog);

    /// <summary>减速停止点动。</summary>
    Task<CommandResult> StopJogAsync();

    /// <summary>立即停止点动。</summary>
    Task<CommandResult> StopJogImmediateAsync();

    /// <summary>
    /// 设置模式
    /// </summary>
    /// <returns></returns>
    Task<CommandResult> SetMode(double mode);

    // ── 驱动控制 ──

    /// <summary>上/下使能（1/0）。</summary>
    Task<CommandResult> RobotEnableAsync(double enabled);

    /// <summary>查询连接状态，Result[0]=1/0。</summary>
    Task<CommandResult> QueryIsConnectedAsync();

    /// <summary>获取当前状态。</summary>
    Task<CommandResult> GetStateAsync();

    /// <summary>切换驱动（真实/仿真）。</summary>
    Task<CommandResult> SwitchDriverAsync(double driver);

    // ── 扫查流程 ──

    /// <summary>开始预扫描。</summary>
    Task<CommandResult> PreScanStartAsync();

    /// <summary>结束预扫描。</summary>
    Task<CommandResult> PreScanEndAsync();

    /// <summary>记录当前位姿为扫查起点。</summary>
    Task<CommandResult> SetStartPoseAsync();

    /// <summary>记录当前位姿为扫查终点。</summary>
    Task<CommandResult> SetEndPoseAsync();

    /// <summary>开始路径规划。</summary>
    Task<CommandResult> PlanAsync();

    /// <summary>执行扫查。</summary>
    Task<CommandResult> ExecuteAsync();

    /// <summary>停止所有运动。</summary>
    Task<CommandResult> StopAsync();

    /// <summary>暂停。</summary>
    Task<CommandResult> PauseAsync();

    /// <summary>恢复。</summary>
    Task<CommandResult> ResumeAsync(double mode=1, double enable=1);

    /// <summary>复位流程。</summary>
    Task<CommandResult> ResetAsync(double mode = 1, double enable = 1);

    /// <summary>查询预扫描是否完成，Result[0]=1/0。</summary>
    Task<CommandResult> QueryPreScanDoneAsync();

    /// <summary>查询运动是否完成，Result[0]=1/0。</summary>
    Task<CommandResult> QueryMotionDoneAsync();

    // ── 仿真控制（仅 Sim 驱动） ──

    /// <summary>设置仿真倍速。</summary>
    Task<CommandResult> SetTimeSpeedAsync(double speed);

    /// <summary>查询仿真倍速，Result[0]=speed。</summary>
    Task<CommandResult> GetTimeSpeedAsync();

    /// <summary>查询仿真时间，Result[0]=time。</summary>
    Task<CommandResult> GetSimTimeAsync();

    /// <summary>查询仿真帧率，Result[0]=fps。</summary>
    Task<CommandResult> GetFrameRateAsync();

    /// <summary>单步仿真。</summary>
    Task<CommandResult> StepOnceAsync();
}

/// <summary>点动参数。</summary>
public sealed record JogParameters(
    int RefFrame,      // 0=关节, 2=基坐标, 4=工具
    int Axis,          // 1~6
    int Direction,     // 0=负, 1=正
    int Speed,         // 0~100
    int Acceleration,  // 0~100
    double MaxDistance // ≥0，0=无限
);
