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

    /// <summary>最新一帧点云（只保留最新；未开流 / 未收到时为 null）</summary>
    SensorPointCloudFrame? LatestSensorFrame { get; }

    /// <summary>连接 / 断线通知</summary>
    event Action<bool>? ConnectionChanged;

    /// <summary>状态流更新（/state 通道，后台线程触发）</summary>
    event Action<BridgeProtocol.StateFrame>? StateUpdated;

    /// <summary>
    /// 感知流更新（/sensor 通道，**后台线程**触发；覆盖式 —— 处理慢了就丢帧）。
    /// 回调里只该把帧交给渲染侧的邮箱，不要在这里等锁 / 做重活。
    /// </summary>
    event Action<SensorPointCloudFrame>? SensorFrameReceived;

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

    /// <summary>开启 /sensor 感知流（需要点云时；未开流时后端不发数据）。</summary>
    void StartSensorStream();

    /// <summary>关闭 /sensor 感知流。</summary>
    void StopSensorStream();

    /// <summary>下发任意指令（扩展用）。<paramref name="text"/> 是字符串参数通道（协议 v0.5）。</summary>
    Task<CommandResult> SendAsync(string cmd, double[]? args = null,
        int timeoutMs = 5000, CancellationToken ct = default, string? text = null);

    /// <summary>
    /// 关节空间运动（q1..q6）。可选速度 / 加速度（**比例 0~1**，null = 不携带、用后端默认）。
    /// 协议里速度 / 加速度是参数尾部可选的两项，且是比例而非百分比。
    /// </summary>
    Task<CommandResult> MoveJAsync(double[] joints, double? speed = null, double? acc = null);

    /// <summary>笛卡尔直线运动（x,y,z,rx,ry,rz）。可选速度 / 加速度（比例 0~1，null = 用后端默认）。</summary>
    Task<CommandResult> MoveLAsync(double[] pose, double? speed = null, double? acc = null);

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

    /// <summary>切换驱动。编码与后端一致：0 = 仿真（sim）/ 1 = 真实（real）。</summary>
    Task<CommandResult> SwitchDriverAsync(RobotDriver driver);

    /// <summary>查询当前驱动类型（无参），Result[0] = 0（仿真）/ 1（真实）。</summary>
    Task<CommandResult> QueryDriverTypeAsync();

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

    // ── 录制（路由到 RECORDER，旁路，与手动/自动模式无关） ──
    // result 定长 7 项：state / records / payload_mib / file_mib / dropped / throttled / files；
    // strings = [文件名]。state：0=stopped、1=recording、2=failed。

    /// <summary>开始录制（打开新文件，绝不覆盖）。</summary>
    Task<CommandResult> RecorderStartAsync();

    /// <summary>停止录制（排空队列并封存，文件立即可回放 / 体检）。</summary>
    Task<CommandResult> RecorderStopAsync();

    /// <summary>查询录制状态（前端"录制中"指示与计时数据源）。</summary>
    Task<CommandResult> RecorderStatusAsync();

    // ── 回放（❌ 已废弃：回放职责移交前端） ──
    // 前端不再通过后端回放：直接读 <records_dir>/*.rusrec 自行解码/播放（见 RUSTool.Replay）。
    // 以下接口仅"暂留过渡"，新代码不要使用。

    [Obsolete(ReplayDeprecation.Message)]
    Task<CommandResult> ReplayListAsync();

    [Obsolete(ReplayDeprecation.Message)]
    Task<CommandResult> ReplayLoadAsync(int? index = null);

    [Obsolete(ReplayDeprecation.Message)]
    Task<CommandResult> ReplayLoadPathAsync(string path);

    [Obsolete(ReplayDeprecation.Message)]
    Task<CommandResult> ReplayStartAsync(double? speed = null);

    [Obsolete(ReplayDeprecation.Message)]
    Task<CommandResult> ReplayPauseAsync();

    [Obsolete(ReplayDeprecation.Message)]
    Task<CommandResult> ReplayResumeAsync();

    [Obsolete(ReplayDeprecation.Message)]
    Task<CommandResult> ReplayStopAsync();

    [Obsolete(ReplayDeprecation.Message)]
    Task<CommandResult> ReplaySeekAsync(double seconds);

    [Obsolete(ReplayDeprecation.Message)]
    Task<CommandResult> ReplaySetSpeedAsync(double speed);

    [Obsolete(ReplayDeprecation.Message)]
    Task<CommandResult> ReplayStepAsync(int count = 1);

    [Obsolete(ReplayDeprecation.Message)]
    Task<CommandResult> ReplayStatusAsync();
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
