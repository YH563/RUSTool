using System;

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

    /// <summary>增量网格块帧（可靠有序队列，不丢块；默认关）</summary>
    public const string Mesh = "/mesh";

    /// <summary>面元点云图（重建融合地图快照，覆盖式；线格式同 /sensor）</summary>
    public const string PcMap = "/pcmap";
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

    /// <summary>
    /// 半自动建图完成（前端下发，路由到 PLANNING）：planning 抓地图快照初始化后才放行 plan。
    /// <c>pre_scan_end</c> 是它的等价兼容别名（见 CommandAlignment_Plan F1）。
    /// </summary>
    public const string PreScanDone = "pre_scan_done";

    // ── 感知 / 重建控制（F6：登记，按需调用） ──
    public const string MapClear = "map_clear";
    public const string LoadCloud = "load_cloud";

    // ── 工具坐标系 / 标定 ──
    public const string SetToolCalibPoint = "set_tool_calib_point";
    public const string ComputeToolCalib = "compute_tool_calib";
    public const string SetToolCoord = "set_tool_coord";
    public const string SetToolIndex = "set_tool_index";
    public const string GetToolCoords = "get_tool_coords";
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

    // ── 录制开关指令（路由到 RECORDER，旁路，与手动/自动模式无关） ──
    public const string RecorderStart = "recorder_start";
    public const string RecorderStop = "recorder_stop";
    public const string RecorderStatus = "recorder_status";

    // ── 回放指令（❌ 后端回放已废弃：回放职责移交前端） ──
    // 新链路：前端走共享文件系统直读 <records_dir>/*.rusrec 自行解码 / 播放。
    // 本节常量与后端 replayer 仅"暂留过渡"，不要在新代码里使用（见 WsProtocol.md §4.7）。

    [Obsolete(ReplayDeprecation.Message)]
    public const string ReplayList = "replay_list";
    [Obsolete(ReplayDeprecation.Message)]
    public const string ReplayLoad = "replay_load";
    [Obsolete(ReplayDeprecation.Message)]
    public const string ReplayLoadPath = "replay_load_path";
    [Obsolete(ReplayDeprecation.Message)]
    public const string ReplayStart = "replay_start";
    [Obsolete(ReplayDeprecation.Message)]
    public const string ReplayPause = "replay_pause";
    [Obsolete(ReplayDeprecation.Message)]
    public const string ReplayResume = "replay_resume";
    [Obsolete(ReplayDeprecation.Message)]
    public const string ReplayStop = "replay_stop";
    [Obsolete(ReplayDeprecation.Message)]
    public const string ReplaySeek = "replay_seek";
    [Obsolete(ReplayDeprecation.Message)]
    public const string ReplaySetSpeed = "replay_set_speed";
    [Obsolete(ReplayDeprecation.Message)]
    public const string ReplayStep = "replay_step";
    [Obsolete(ReplayDeprecation.Message)]
    public const string ReplayStatus = "replay_status";
}

/// <summary>后端回放废弃说明（统一文案，供各处 <c>[Obsolete]</c> 引用）。</summary>
public static class ReplayDeprecation
{
    public const string Message =
        "后端回放已废弃（移交前端）：前端本地读 <records_dir>/*.rusrec 自行回放，仅过渡期保留。";
}

/// <summary>异步事件名（与后端 string_consts.hpp 的 EventName 对齐）</summary>
public static class Events
{
    public const string PreScanDone = "pre_scan_done";
    public const string PlanDone = "plan_done";
    public const string ScanDone = "scan_done";
    public const string MotionDone = "motion_done";
    public const string Error = "error";

    /// <summary>回放播到末尾（<c>loop=false</c>）时由回放节点广播；<c>ack_id</c> = 触发播放的指令 id。</summary>
    [Obsolete(ReplayDeprecation.Message)]
    public const string ReplayDone = "replay_done";
}

/// <summary>感知帧类型（与后端 string_consts.hpp 的 SensorType 对齐）</summary>
public static class SensorTypes
{
    /// <summary>点云（已接通：<see cref="SensorFrameCodec"/> 解码）。<c>/pcmap</c> 也用它（<c>scope=map</c>）。</summary>
    public const string PointCloud = "pointcloud";

    /// <summary>图像（通道已开、解码未实现）</summary>
    public const string Image = "image";

    /// <summary>压缩图（通道已开、解码未实现）</summary>
    public const string Compressed = "compressed";

    /// <summary>增量网格块（<c>/mesh</c> 通道，<see cref="MeshFrameCodec"/> 解码）</summary>
    public const string Mesh = "mesh";

    /// <summary>超声（预留，未实现）</summary>
    public const string Ultrasound = "ultrasound";
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
