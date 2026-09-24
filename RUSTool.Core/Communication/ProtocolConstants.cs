namespace RUSTool.Communication;

/// <summary>WebSocket 通道路径（与后端 string_consts.hpp 的 WsPath 对齐）</summary>
public static class Channels
{
    /// <summary>command / reply / event</summary>
    public const string Control = "/control";

    /// <summary>state 高频流（可丢帧）</summary>
    public const string State = "/state";

    /// <summary>感知二进制帧（可丢帧，覆盖式：只发最新一帧）</summary>
    public const string Sensor = "/sensor";
}

/// <summary>指令名（与后端 string_consts.hpp 的 CmdName 对齐）</summary>
public static class Commands
{
    // ── 任务级指令 ──
    public const string Connect = "connect";
    public const string Shutdown = "shutdown";
    public const string SetMode = "set_mode";
    public const string PreScanStart = "pre_scan_start";
    public const string PreScanEnd = "pre_scan_end";
    public const string SetStartPose = "set_start_pose";
    public const string SetEndPose = "set_end_pose";
    public const string Plan = "plan";
    public const string Execute = "execute";
    public const string Stop = "stop";
    public const string Pause = "pause";
    public const string Resume = "resume";
    public const string Reset = "reset";
    public const string QueryPreScanDone = "query_prescan_done";
    public const string QueryMotionDone = "query_motion_done";

    // ── 运动控制指令 ──
    public const string MoveJ = "movej";
    public const string MoveL = "movel";
    public const string ServoJ = "servoj";
    public const string ServoCart = "servo_cart";
    public const string StartJog = "start_jog";
    public const string StopJogDecel = "stop_jog_decel";
    public const string StopJogImmediate = "stop_jog_immediate";
    public const string ServoStart = "servo_start";
    public const string ServoEnd = "servo_end";

    // ── 驱动控制指令 ──
    public const string Disconnect = "disconnect";
    public const string IsConnected = "is_connected";
    public const string IsInDragTeach = "is_in_drag_teach";
    public const string RobotEnable = "robot_enable";
    public const string GetState = "get_state";
    public const string IsMotionDone = "is_motion_done";
    public const string RunFile = "run_file";

    /// <summary>切换驱动。args = [type]（type 0 = 仿真 / 1 = 真实）。</summary>
    public const string SwitchDriver = "switch_driver";

    /// <summary>查询当前驱动类型。无参，Result[0] = 0（仿真）/ 1（真实），与 <see cref="SwitchDriver"/> 的 type 同编码。</summary>
    public const string GetDriverType = "get_driver_type";

    // ── 仿真控制指令（仅 Sim 驱动） ──
    public const string SetTimeSpeed = "set_time_speed";
    public const string GetTimeSpeed = "get_time_speed";
    public const string GetSimTime = "get_sim_time";
    public const string StepOnce = "step_once";
    public const string GetFrameRate = "get_frame_rate";
}

/// <summary>异步事件名（与后端 string_consts.hpp 的 EventName 对齐）</summary>
public static class Events
{
    public const string PreScanDone = "pre_scan_done";
    public const string PlanDone = "plan_done";
    public const string ScanDone = "scan_done";
    public const string MotionDone = "motion_done";
    public const string Error = "error";
}

/// <summary>感知帧类型（与后端 string_consts.hpp 的 SensorType 对齐）</summary>
public static class SensorTypes
{
    /// <summary>点云（已接通：<see cref="SensorFrameCodec"/> 解码）</summary>
    public const string PointCloud = "pointcloud";

    /// <summary>图像（通道已开、解码未实现）</summary>
    public const string Image = "image";

    /// <summary>压缩图（通道已开、解码未实现）</summary>
    public const string Compressed = "compressed";
}

/// <summary>
/// 感知帧 payload 的压缩算法（与后端 string_consts.hpp 对齐；协议 §3.3 的 <c>encoding</c>）。
/// 客户端必须按头字段分支，不能写死 zstd —— 它是协议字段，不是实现细节。
/// </summary>
public static class SensorEncodings
{
    public const string Zstd = "zstd";
    public const string Raw = "raw";
}

/// <summary>感知帧的数据语义（协议 §3.3 的 <c>scope</c>；两者都是自包含的完整点集，一律整帧替换）</summary>
public static class SensorScopes
{
    /// <summary>单视角当前帧</summary>
    public const string Frame = "frame";

    /// <summary>累积地图快照（点数远大于单帧，协议里没有 delta 字段）</summary>
    public const string Map = "map";
}
