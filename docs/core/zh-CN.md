# RUSTool.Core（简体中文）

> 状态：反映当前实现。配套：`../README.md`（仓库入口）、`../architecture/zh-CN.md`（架构与 ADR）、`../protocol/zh-CN.md`（bridge 协议）、`../ui/zh-CN.md`（界面层）、`../testing/zh-CN.md`（怎么跑测试）。

`RUSTool.Core` 是本仓库的**纯逻辑层**：对外只提供四类东西 —— 通信客户端、机器人业务服务、流程状态机、日志契约。它不认识 Avalonia / XAML / Silk.NET / OpenGL，也不认识 LiveCharts / SkiaSharp，因此可以在没有界面、没有显卡、没有网络的情况下被单测覆盖。

---

## 1. 模块总览

| 命名空间 | 目录 | 职责 | 关键类型 |
|---|---|---|---|
| `RUSTool.Communication` | `Communication/` | 与 bridge 的传输：三条通道的连接、指令 / 回执匹配、状态流与感知流接收、二进制帧解码、断线重连 | `BridgeClient`、`ConnectionManager`、`BridgeProtocol`、`SensorFrameCodec`、`Channels` / `Commands` / `Events` / `SensorTypes` |
| `RUSTool.Services.Robot` | `Services/Robot/` | 机器人业务：类型化指令 + 共享会话状态与模式互斥 | `IRobotService`、`RobotService`、`RobotSession`、`JogParameters` |
| `RUSTool.Services.Robot.Workflows` | `Services/Robot/Workflows/` | 流程编排：把无状态指令串成有顺序的流程（只描述「状态怎么变」） | `ScanStateMachine`、`ScanStage`、`ScanTrigger` |
| `RUSTool.Services.Logging` | `Services/Logging/` | 日志契约（实现留在界面层） | `ILogService`、`LogEntry`、`LogLevel` |

```
RUSTool.UI（界面层）
     │ 只依赖这些接口 / 门面
     ▼
┌──────────────────────────────────────────────────────────┐
│ RUSTool.Core                                             │
│  IRobotService ──► RobotService ──┐                      │
│  ILogService（契约）               │                      │
│  RobotSession（共享状态 + 仲裁）    ▼                      │
│  ScanStateMachine（纯状态机）  BridgeClient（唯一门面）      │
│                                    │                     │
│                                    ▼                     │
│                         ConnectionManager（三条 WebSocket）│
└──────────────────────────────────────────────────────────┘
                                     │
                                     ▼
                    后端 bridge（/control /state /sensor）
```

> **命名空间与工程名刻意不一致**：工程叫 `RUSTool.Core`，但类型仍在 `RUSTool.*` 下 —— 拆分工程之前写下的 `using` 一行都不用改（`RUSTool.Core.csproj` 的 `RootNamespace` 也设成了 `RUSTool`，保证今后新增文件不会落到 `RUSTool.Core.*`）。
>
> 本层只有两个第三方依赖，都不碰 UI 框架：
> `CommunityToolkit.Mvvm`（只用 `ObservableObject` 一个基类，即 `INotifyPropertyChanged` 的实现）与
> `ZstdSharp.Port`（`/sensor` 点云帧的 zstd 解压；纯托管、无本机依赖）。两者都不违反「Core 不认识界面」这条约束。

---

## 2. 通信客户端：类总览

协议契约以 [`../protocol/zh-CN.md`](../protocol/zh-CN.md) 为准，客户端负责：

1. 建立 `/control`（必连）、`/state` 与 `/sensor`（按需）三条 WebSocket 通道；
2. 指令下发 + 回执匹配（reply 按 `id`，event 按 `ack_id`）；
3. 状态流接收（只保留最新一帧）；
4. 感知二进制帧解码（点云，覆盖式只保留最新一帧）；
5. 断线重连。

```
┌──────────────────────────────────────────────────────┐
│ BridgeClient（对外门面）                                │
│   客户端库唯一的公共入口，供上层（UI/VM）调用              │
└───────────┬───────────────────────────┬──────────────┘
            │ 调用                       │ 消息到达
┌───────────▼────────────┐  ┌───────────▼───────────────┐
│ ConnectionManager      │  │ BridgeProtocol（静态）      │
│ WebSocket 连接 / 收发 / │  │ JSON 消息模型 + 编解码       │
│ 重连（三条通道）         │  ├───────────────────────────┤
└───────────┬────────────┘  │ SensorFrameCodec（静态）    │
            │ 原始字节        │ /sensor 二进制帧解码        │
            ▼               └───────────────────────────┘
   WebSocket 传输（/control · /state · /sensor）
```

四个类，职责单一，无多余抽象：

| 类 | 可见性 | 职责 | 依赖 |
|----|------|------|------|
| `BridgeClient` | `public` | 对外 API：指令下发、事件订阅、状态 / 感知获取；内部做请求追踪 | `ConnectionManager`, `BridgeProtocol`, `SensorFrameCodec` |
| `ConnectionManager` | `internal` | 连接建立 / 收发循环 / 断线重连 / 退避 | 无（用 `ClientWebSocket`） |
| `BridgeProtocol` | `public`（静态） | 消息模型定义 + JSON 编解码（纯函数） | 无 |
| `SensorFrameCodec` | `public`（静态） | `/sensor` 二进制帧：自检 + 解压 + 反量化（纯函数） | `ZstdSharp` |

> `BridgeProtocol` 管 JSON 那条通路，`SensorFrameCodec` 管二进制那条通路 —— 两者都是「无状态纯函数」，
> 因此都能在无网络、无界面、无显卡的单测里覆盖（见第 12 节）。

---

## 3. 消息模型（BridgeProtocol.cs）

对应协议 §2 / §3。reply 与 event 同构，用一个类；状态帧单独一类。

```csharp
public static class BridgeProtocol
{
    // ---- 请求（前端 → 后端）----
    public sealed record Command(uint Id, string Cmd, double[] Args);

    // ---- 回执 / 事件（后端 → 前端，同构）----
    public sealed record ReplyOrEvent(
        string Type,        // "reply" / "event"
        uint Id,            // reply: 对应 command id；event: 恒 0
        uint AckId,         // 仅 event：触发它的 command id
        string Event,       // 仅 event：事件名
        bool Success,
        string Message,
        double[] Result);

    // ---- 状态帧（/state 通道）----
    public sealed record StateFrame(
        double Timestamp, double FrameRate,
        double[] JointPos, double[] JointVel, double[] JointAcc,
        double[] Effort, double[] FlangePos);
}
```

编解码（静态方法）：

```csharp
    public static string Encode(Command cmd);                          // 序列化 command
    public static ReplyOrEvent? TryParseReply(string json);            // 解析 reply / event
    public static StateFrame? TryParseState(string json);              // 解析 state 帧
```

> 各连接收到的 JSON 先经 `TryParseReply` / `TryParseState` 分流；解析失败直接丢弃并记录日志。
> `/state` 与 `/control` 走上面这套；`/sensor` 是二进制帧，走 §3.1 的 `SensorFrameCodec`（两套互不干扰）。

常量放 `ProtocolConstants.cs`：

```csharp
public static class Channels  { public const string Control = "/control"; public const string State = "/state"; public const string Sensor = "/sensor"; }
public static class Commands  { public const string MoveJ = "movej"; /* 与 command_defs.hpp 对齐 */ }
public static class Events    { public const string PlanDone = "plan_done"; /* 与事件清单对齐 */ }
public static class SensorTypes    { public const string PointCloud = "pointcloud"; /* image / compressed 未解码 */ }
public static class SensorEncodings { public const string Zstd = "zstd"; public const string Raw = "raw"; }
public static class SensorScopes    { public const string Frame = "frame"; public const string Map = "map"; }
```

### 3.1 感知帧解码（SensorFrameCodec.cs）

`/sensor` 的线格式与解码契约以 [`../protocol/zh-CN.md`](../protocol/zh-CN.md) 第 5 节为准，
这里是它在 Core 里的样子（纯函数、无状态、可单测）：

```csharp
public static class SensorFrameCodec
{
    public const int PointStride = 10;        // int16 x | int16 y | int16 z | uint32 rgb
    public const int MaxPoints   = 4_000_000; // 守卫：头里点数超上限 → 整帧丢

    // 一条 WS 消息 = 一帧；失败返回 null + 中文原因（不抛）
    public static SensorPointCloudFrame? TryDecode(ReadOnlyMemory<byte> message, out string? error);

    // 反量化 / 量化（与后端 SensorEncoder::quantize 严格互逆）
    public static float Dequantize(short q, double min, double max);
    public static short Quantize(double value, double min, double max);

    // 组装一条 raw 帧：单测与截图模式的合成点云用（客户端本来只解码）
    public static byte[] EncodeRaw(ReadOnlySpan<float> xyz, ReadOnlySpan<uint> rgb, int count, …);
}

public sealed record SensorPointCloudFrame(
    float[] Xyz, uint[] Rgb, int Count,
    uint Seq, double Timestamp,
    string FrameId, string Encoding, string Scope);
```

三条设计取舍：

1. **坏帧一律丢，不抛异常**：返回 `null` + 原因，由调用方决定记不记日志。
   覆盖式通道下坏帧可能每帧都来，`BridgeClient` 因此只记第 1 次与之后每 100 次。
2. **不做任何坐标变换**：帧头 `frame_id = base_link`，后端已经算好变换 ——
   客户端多算一次就等于把点云搬到另一个位姿上。
3. **整数契约用显式小端读**：`BinaryPrimitives.Read*LittleEndian`，不用 `BitConverter` 的本机端序，
   也不用 `Marshal`/`MemoryMarshal.Cast` 这类「看本机端序」的捷径。

---

## 4. 对外门面：BridgeClient

```csharp
public sealed class BridgeClient : IDisposable
{
    /// <summary>host 默认 127.0.0.1，port 默认 8765</summary>
    public BridgeClient(string host = "127.0.0.1", ushort port = 8765);

    // ---- 连接 ----
    public bool IsConnected { get; }              // /control 是否在线
    public event Action<bool>? ConnectionChanged; // 连接/断线通知

    public Task ConnectAsync();                   // 连 /control（必连），断线自动重连
    public void StartStateStream();               // 需要状态可视化时调用，连 /state
    public void StopStateStream();
    public void StartSensorStream();              // 需要点云时调用，连 /sensor（未连则后端不发数据）
    public void StopSensorStream();

    // ---- 指令（阻塞式，等 reply 返回）----
    public Task<CommandResult> SendAsync(string cmd, double[]? args = null,
        int timeoutMs = 5000, CancellationToken ct = default);

    // ---- 异步事件（长任务完成通知，如 plan_done）----
    public event Action<EventNotification>? EventReceived;

    // ---- 状态流（只保留最新一帧）----
    public StateFrame? LatestState { get; }
    public event Action<StateFrame>? StateUpdated;

    // ---- 感知流（/sensor，已在 WS 线程解好码；覆盖式只保留最新一帧）----
    public SensorPointCloudFrame? LatestSensorFrame { get; }
    public event Action<SensorPointCloudFrame>? SensorFrameReceived;

    // ---- 指令日志回调（组装层注入，例如接到全局日志服务）----
    public Action<string, bool>? Logger { get; set; }   // (message, isError)
}
```

对上层友好的返回值封装（屏蔽协议原始字段）：

```csharp
public sealed record CommandResult(bool Success, string Message, double[] Result);
public sealed record EventNotification(string EventName, bool Success, string Message);
```

**内部实现要点**（都藏在 BridgeClient 里，不拆类）：

- 指令下发：`SendAsync` 内部 id 自增 → 序列化 → 发给 `/control` → 以
  `ConcurrentDictionary<uint, TaskCompletionSource<CommandResult>>` 挂起等待；
  reply 按 `id` 匹配并完成对应 TCS。
- 长任务闭环：指令先收 reply（成功 = 已受理），之后收到 `event` 时广播 `EventReceived`
  （`EventName` / `Success` / `Message`）。**回执与事件是两条独立信号** ——
  协议里的 `ack_id` 会被解析进 `ReplyOrEvent`，但当前实现不据此做请求关联，
  上层按事件名自己订阅即可（`ScanWorkflowViewModel` 就是这么做的）。
- 超时：`timeoutMs`（默认 5000）内未收到 reply → 返回 `Success=false, Message="timeout"`，
  并从字典移除。
- 断线：`ConnectionChanged(false)` 通知上层，所有未决请求全部置为失败（`Message="connection lost"`）。
- 日志：`Logger` 回调把「→ 发送指令 …」「← 指令 … 结果: …」写成项目日志（来源列 `bridge`），
  由组装层注入 —— Core 自己不认识 `ILogService` 的实现。

---

## 5. 连接管理：ConnectionManager

```csharp
internal sealed class ConnectionManager : IDisposable
{
    public ConnectionManager(string host, ushort port);

    public bool IsControlConnected { get; }                             // /control 是否在线
    public event Action<ReadOnlyMemory<byte>>? ControlMessageReceived;   // /control 通道整帧
    public event Action<ReadOnlyMemory<byte>>? StateMessageReceived;     // /state 通道整帧
    public event Action<ReadOnlyMemory<byte>>? SensorMessageReceived;    // /sensor 通道整帧（二进制，未解码）
    public event Action? ControlConnected;                              // 首次连上 / 重连成功
    public event Action? ControlDisconnected;                           // /control 断线
    public event Action? StateDisconnected;                             // /state 断线（重连归 BridgeClient）
    public event Action? SensorDisconnected;                            // /sensor 断线（重连归 BridgeClient）

    public Task ConnectControlAsync(CancellationToken ct);               // 建连 + 启动重连循环
    public Task ConnectStateAsync(CancellationToken ct);                 // 单次尝试，不自动重连
    public Task DisconnectStateAsync();
    public Task ConnectSensorAsync(CancellationToken ct);                // 同上，/sensor
    public Task DisconnectSensorAsync();
    public Task SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct);  // 走 /control
    public void DisconnectAll();
}
```

设计要点：

- **每个通道一个 `ClientWebSocket` + 一个接收循环**（`/control`、`/state`、`/sensor` 各一对），
  一个收一个发，不共用一个循环 —— 避免大 payload（兆级点云帧）阻塞指令。
- **整帧接收**：`ReceiveFrameAsync` 循环收片段直到 `EndOfMessage`，拼成一条完整消息再抛事件；
  高频状态帧下用 `ArrayPool<byte>.Shared` 租 64KB 缓冲，避免每帧分配。
- **重连退避**：`/control` 断线后按 `ReconnectDelaysMs = [500, 1000, 2000, 5000, 10000]` ms
  依次重试并封顶，连上即重置；`/state` 与 `/sensor` 断线由 `BridgeClient` 的
  `StartStateLoop` / `StartSensorLoop` 再次触发连接（两条流各一把「同一时刻只允许一个重连循环」的闸，
  退避固定 1s，防止断线风暴）。
- **`/sensor` 收的是大帧**：整帧拼好后才抛 `SensorMessageReceived`，解码（解压 + 反量化）
  也就在这条循环的线程上做完再交给上层 —— 于是 UI 线程永远不碰几 MB 的 payload。
- **未连接时发送会抛** `InvalidOperationException("控制通道未连接")` —— 由 `BridgeClient`
  转成一次失败的 `CommandResult`，不让异常穿透到 VM。

---

## 6. 客户端状态机与时序

### 6.1 状态机

```
      ┌─────────┐   ConnectAsync    ┌──────────┐   WS 连上   ┌─────────┐
      │ Created │ ────────────────→ │ Connecting │ ─────────→ │Connected│
      └─────────┘                   └──────────┘             └────┬────┘
                                                                  │ 断线
                                               ┌──────────────────┘
                                               ▼
                                          ┌──────────┐   自动重连   ┌─────────┐
                                          │Disconnected│ ─────────→ │Connecting│
                                          └──────────┘               └─────────┘
```

- `Connected`：可发指令、可收数据。
- `Disconnected`：未决请求全部失败，`ConnectionChanged(false)` 通知上层；重连循环自动进行。

### 6.2 时序示例

**同步指令（get_state）：**

```
UI/Sender         BridgeClient              ConnectionManager        bridge
   │ SendAsync("get_state")                     │                      │
   │ ──────────────→   id=5 挂起 pending         │  Encode → Send       │
   │                    ───────────────────────→│────────────────────→ │
   │                    │                       │                      │
   │                    │←──── JSON ────────────│←─────────── reply(id=5)
   │                    │ 按 id=5 匹配，完成 TCS │                      │
   │ ←── CommandResult ──┘                      │                      │
```

**长任务（plan → plan_done）：**

```
   │ SendAsync("plan")                          │                      │
   │ ──────→  id=6 挂起                         │── command(id=6) ───→│
   │          │←── reply(id=6, success=true) ←──│←────────────────────│ 受理
   │ ←── CommandResult(success) ──┘             │                      │
   │          │                                 │                      │
   │          │←── event(ack_id=6, plan_done) ←─│←────── module_events │ 完成
   │ ←── EventReceived(plan_done) ──┘           │                      │
```

---

## 7. 业务服务：IRobotService / RobotService

界面层**只**认识这个接口 —— 它把「协议字符串」这件脏活全部关在 `RobotService` 里，
所以换协议、换传输、换后端（真机 / 仿真 / 回放）都不用动 VM。

| 方法族 | 方法 | 对应指令 |
|---|---|---|
| 连接 | `ConnectAsync` / `Disconnect` / `StartStateStream` / `StopStateStream` / `StartSensorStream` / `StopSensorStream` | 起 `BridgeClient` 的连接与两条按需流（不发指令） |
| 通用 | `SendAsync(cmd, args…)` | 任意指令（扩展用；新协议字段先加在 `ProtocolConstants`） |
| 运动 | `MoveJAsync` / `MoveLAsync` | `movej` / `movel` |
| 点动 | `StartJogAsync(JogParameters)` / `StopJogAsync` / `StopJogImmediateAsync` | `start_jog` / `stop_jog_decel` / `stop_jog_immediate` |
| 模式 | `SetMode(double)` | `set_mode`（0 = 手动 / 1 = 扫查） |
| 驱动 | `RobotEnableAsync` / `QueryIsConnectedAsync` / `GetStateAsync` / `SwitchDriverAsync(RobotDriver)` / `QueryDriverTypeAsync` | `robot_enable` / `is_connected` / `get_state` / `switch_driver` / `get_driver_type` |
| 扫查流程 | `PreScanStartAsync` / `PreScanEndAsync` / `SetStartPoseAsync` / `SetEndPoseAsync` / `PlanAsync` / `ExecuteAsync` | `pre_scan_start` / `pre_scan_end` / `set_start_pose` / `set_end_pose` / `plan` / `execute` |
| 流程控制 | `StopAsync` / `PauseAsync` / `ResumeAsync` / `ResetAsync` / `QueryPreScanDoneAsync` / `QueryMotionDoneAsync` | `stop` / `pause` / `resume` / `reset` / `query_prescan_done` / `query_motion_done` |
| 仿真（仅 Sim 驱动） | `SetTimeSpeedAsync` / `GetTimeSpeedAsync` / `GetSimTimeAsync` / `GetFrameRateAsync` / `StepOnceAsync` | `set_time_speed` / `get_time_speed` / `get_sim_time` / `get_frame_rate` / `step_once` |

三条规则：

1. **每个方法都是无状态的**：调一次，发一条指令，返回 `CommandResult`。顺序、门控、重试都不属于这一层
   （流程交给 `ScanStateMachine` + 界面 VM）。
2. **失败不抛异常**：连接断开 / 超时 / 后端返回 `success=false` 都体现在
   `CommandResult.Success` / `Message` 上，调用方只需写日志与更新状态灯。
3. **点动参数用记录类型**（`JogParameters`），字段顺序即 `start_jog` 的 `args` 顺序：

```csharp
public sealed record JogParameters(
    int RefFrame,      // 0=关节, 2=基坐标, 4=工具
    int Axis,          // 1~6
    int Direction,     // 0=负, 1=正
    int Speed,         // 0~100
    int Acceleration,  // 0~100
    double MaxDistance // ≥0，0=无限
);
```

---

## 8. 共享会话状态：RobotSession

多个 VM 都关心的状态（连接 / 使能 / 驱动 / 暂停 / 操作模式）集中放在这里，
消除「同一状态在多处各存一份」导致的界面不同步。它是 `ObservableObject`（唯一用到 MVVM 基类的地方）。

| 成员 | 类型 | 说明 |
|---|---|---|
| `IsConnected` / `IsEnabled` / `IsPaused` | `bool`（可绑定） | 由连接与指令结果驱动 |
| `Driver` | `RobotDriver` | `Simulation`(0) / `Real`(1)，**取值即后端编码**（默认仿真） |
| `IsDriverKnown` | `bool` | 驱动类型是否已从后端回读：未连接 / 掉线 / 回读失败一律 `false` |
| `Mode` | `RobotMode` | `Idle` / `Manual` / `Scan`，**外部只能通过 `TryEnter*` / `ExitToIdle` 改变** |
| `ModeChanged` | 事件 | 供各 VM 刷新 `CanExecute` |
| `DriverText` / `ConnectText` / `EnableText` / `ModeText` | `string` | 工具栏直接绑定的中文文本（派生量，无转换器）；`DriverText` 在 `IsDriverKnown=false` 时是「未知」 |

> **「未知」是一等状态。** 驱动类型是**后端的状态**：没连上就无从查起，
> 所以 `IsDriverKnown=false` 时 `DriverText` 显示「未知」，工具栏的两个驱动按钮一起变灰 ——
> 不知道是哪个驱动时，任何「已选中」都是在撒谎。
> 连接成功 / 断线重连成功 / 切换驱动之后由 `SessionViewModel` 回读 `get_driver_type`，
> 只有回读成功才把 `Driver` 与 `IsDriverKnown` 一起写进去；掉线立刻置回未知。

写者约定（**唯一 owner**，避免竞态）：

- `IsConnected` / `IsEnabled` / `Driver` / `IsDriverKnown` —— `SessionViewModel`（连接类动作的唯一入口）；
- `Mode` —— 本类的 `TryEnterManual()` / `TryEnterScan()` / `ExitToIdle()`；
- `IsPaused` —— 暂停 / 恢复命令；
- 其余 VM **只读**。

> **接线现状**：目前只有 `ExitToIdle()` 被调用（急停 / 复位路径），
> `TryEnterManual()` / `TryEnterScan()` 还没有调用方，因此 `ModeText` 实际只会显示
> 「空闲 / 已暂停」。把模式仲裁接进 `RobotControlViewModel` / `ScanWorkflowViewModel`
> 是下一步（见 [`../architecture/zh-CN.md`](../architecture/zh-CN.md) 第 7 节）。

---

## 9. 流程编排：ScanStateMachine

`IRobotService` 的每个方法都是无状态的，但界面上的「预扫查 → 位姿 → 规划 → 执行」是**有顺序**的。
用 5 个 bool 表达这 4 步有 32 种组合、其中 27 种非法；换成 8 个阶段的枚举后，非法状态在类型上就无法表示。

| 阶段 | 含义 |
|---|---|
| `Idle` | 待开始（刚进入流程或已复位） |
| `PreScanning` | ① 预扫描进行中 |
| `Posing` | ② 标定位姿：正在记录起点 / 终点（两者都记好才能规划） |
| `Planning` | ③ 已下发 `plan`，等 `plan_done` |
| `Ready` | ③ 规划完成，可以下发 `execute`（必须存在，否则「规划完成」与「开始执行」之间无处可停留） |
| `Executing` | ④ 扫查执行中，等 `motion_done` / `scan_done` |
| `Completed` | 扫查正常完成，可复位重来 |
| `Faulted` | 后端上报 `error`；**只能** `Reset` 回 `Idle`，不支持重试 |

触发源分两类：**用户动作**（`StartPreScan` / `EndPreScan` / `SetStartPose` / `SetEndPose` /
`StartPlan` / `Execute` / `Stop` / `Reset`）与**后端事件**（`PreScanDone` / `PlanDone` / `ScanDone` / `Fail`）。

```
Idle ──► PreScanning ──► Posing ──► Planning ──► Ready ──► Executing ──► Completed
  ▲        ① 预扫描      ② 位姿      ③ 规划               ④ 执行          │
  │                                                                        │
  └────────── Stop / Reset ◄──────── Faulted ◄──────── Fail ◄───────────────┘
```

六条不变量（都被 54 个用例锁住）：

1. **所有状态变化都走 `TryFire(trigger)`**，非法转移返回 `false` 且状态不变，调用方据此放弃发命令。
2. **`CanFire()` 与 `TryFire()` 同一套判定** —— 「按钮灰不灰」与「点了能不能执行」永远一致。
3. `Stop` / `Reset` 在任何阶段都合法 → 回 `Idle` 并清空起点 / 终点进度；`Fail` 任何阶段都合法 → `Faulted`。
4. **子条件门禁**：`Posing` 阶段必须起点**和**终点都记录（`PoseReady`）才允许 `StartPlan`。
5. **回执与事件谁先到都算数**：显式写出的自转移（如 `Planning + PlanDone => Ready`、
   `Ready + PlanDone => Ready`）让迟到的那条信号无害；阶段真变了才触发 `StageChanged`。
6. `PreScanDone` 由阶段**推导**（`Stage is not (Idle or PreScanning)`），不单独存字段。

> **接线现状**：状态机目前只被 `tests/RUSTool.Core.Tests` 使用；界面侧的
> `ScanWorkflowViewModel` 仍以 `ScanStep` 列表 + 回执 / 事件双来源推进步骤。接线计划见路线图。

---

## 10. 日志契约：ILogService / LogEntry

| 类型 | 说明 |
|---|---|
| `LogLevel` | `Debug` / `Info` / `Success` / `Warn` / `Error` 五档（`Success` 专用于后端回执 `success=true`） |
| `LogEntry` | `record`：`Time` / `Level` / `Source` / `Message` + 派生文本 `TimeText` / `LevelText` / `Formatted` |
| `ILogService` | `Entries`（`ObservableCollection<LogEntry>`）/ `Log(message, level, source)` / `Clear()` |

`LogEntry` 还带四个互斥布尔量 `IsDebug` / `IsSuccess` / `IsWarning` / `IsError`，
专供 XAML 的 `Classes.xxx="{Binding …}"` 使用 —— 这样「级别 → 颜色」不需要任何值转换器。
它们只由 `string` / `bool` 组成，不含 UI 类型，所以放在纯逻辑层并不违反约束。

**线程安全约定**：`ILogService.Log` 允许从后台线程调用（`/state` 状态流、图形栈日志都在后台线程），
由**实现**负责 marshal 到 UI 线程 —— 这也是契约留在 Core、实现留在 `RUSTool.UI` 的原因。

---

## 11. 生命周期与使用方式（应用侧）

本仓库里这些类型的生命周期全部由**唯一组合根** `App.CreateMainViewModel` 决定（见 [`../ui/zh-CN.md`](../ui/zh-CN.md)）：

```csharp
// 应用启动时（App.CreateMainViewModel，节选）
var bridge = new BridgeClient();                                  // 默认 127.0.0.1:8765
ILogService log = new LogService();
bridge.Logger = (m, e) => log.Log(m, e ? LogLevel.Error : LogLevel.Info, "bridge");
IRobotService robot = new RobotService(bridge);
var session = new RobotSession();
return new MainViewModel(robot, session, log);

// 连接与状态流由界面动作驱动：
await robot.ConnectAsync();        // 连 /control（失败/断线自动重连）
robot.StartStateStream();          // 需要 HUD 读数时开启 /state
robot.StartSensorStream();         // 需要点云时开启 /sensor（两者都可随时 Stop*）

// 应用退出时
robot.Dispose();                   // 断开所有连接，未决请求置失败
```

Core 内部**不启动任何后台任务**，也不读取任何配置 —— 连不连、什么时候连，完全由上层说了算。

---

## 12. 反例与测试要点

**反例（都会破坏分层或状态一致性）**

| 反例 | 为什么不行 |
|---|---|
| 在 VM 里写 `"movej"` / `"plan"` 等协议字符串 | 协议变更会散落到界面层；应只经 `IRobotService` |
| 在 `RUSTool.Core` 里 `using Avalonia.*` | 编译不过，正是这条约束在起作用 |
| VM 自己 `new BridgeClient()` / `new LogService()` | 破坏「唯一组合根」，换后端要改多处 |
| 绕过 `TryFire()` 直接改 `Stage`（或自己拼 bool 门控） | 非法状态会重新出现；门控与按钮置灰会不一致 |
| 在 VM 里保存 `IsConnected` 的副本 | 与 `RobotSession` 双份状态 → 不同步 |
| 为「级别 → 颜色」写 `IValueConverter` | 主题语义类已覆盖；转换器是本项目刻意不引入的一层 |

**测试要点**

| 对象 | 方式 |
|---|---|
| `BridgeProtocol` | 用协议文档里的样例 JSON 做单元测试（reply / event / state 解析，字段缺省与坏 JSON） |
| `SensorFrameCodec` | **已有 24 个用例**（见 [`../testing/zh-CN.md`](../testing/zh-CN.md)）：raw / zstd 两种编码、量化端点、缺省字段，以及坏帧（头长度不自洽、消息不足 4 字节、JSON 坏了、点数超上限、payload 长度与 points 不符、未知 encoding、zstd 垃圾数据、未实现的帧类型、dtype / fields / 包围盒不符、字段类型不符）—— 坏帧一律返回 `null` + 原因，绝不抛 |
| `BridgeClient.SendAsync` | 注入 fake `ConnectionManager`，验证 id 自增 / reply 匹配 / 超时返回失败 |
| 断线重连 | fake 连接模拟断线，验证退避序列与未决请求置失败 |
| `ScanStateMachine` | **已有 54 个用例**（见 [`../testing/zh-CN.md`](../testing/zh-CN.md)），无需网络 / 界面 / GL |
| `RobotDriverCodec` | **已有 15 个用例**（见 [`../testing/zh-CN.md`](../testing/zh-CN.md)）：0/1 编码与后端逐字一致、越界与 `NaN` / `Infinity` 一律退回仿真、往返一致、指令名逐字一致，以及 `RobotSession` 的「未知」语义（刚构造 / 掉线都显示未知）与 `DriverText` 变更通知 |
| 集成 | 连本地 bridge，实发 `get_state` / `stop`，比对协议文档 |
