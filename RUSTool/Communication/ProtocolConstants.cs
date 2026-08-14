namespace RUSTool.Communication;

/// <summary>WebSocket 通道路径（与后端 string_consts.hpp 的 WsPath 对齐）</summary>
public static class Channels
{
    /// <summary>command / reply / event</summary>
    public const string Control = "/control";

    /// <summary>state 高频流（可丢帧）</summary>
    public const string State = "/state";

    /// <summary>感知二进制帧（可丢帧，预留）</summary>
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
    public const string SwitchDriver = "switch_driver";

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

/// <summary>感知帧类型（与后端 string_consts.hpp 的 SensorType 对齐；通道预留）</summary>
public static class SensorTypes
{
    public const string PointCloud = "pointcloud";
    public const string Image = "image";
    public const string Compressed = "compressed";
}
