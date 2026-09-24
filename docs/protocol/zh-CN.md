# RUSTool bridge 协议（WebSocket，简体中文）

> 状态：反映当前实现。客户端侧的落地实现见 [`../core/zh-CN.md`](../core/zh-CN.md)；本文是**后端联调时的契约基准**。配套：[`../README.md`](../README.md)、[`../architecture/zh-CN.md`](../architecture/zh-CN.md)。

后端（DriverNode）启动时自动在 `ws://localhost:8765` 开启 WebSocket 服务器；
本仓库客户端 `BridgeClient` 的默认地址是 `ws://127.0.0.1:8765`（构造函数参数可改）。

---

## 0. 三条通道

| 通道 | 方向 | 格式 | 现状 |
|---|---|---|---|
| `/control` | 双向 | JSON（command / reply / event） | ✅ 已实现：必连，断线按 500ms → 10s 退避自动重连 |
| `/state` | 服务器 → 客户端 | JSON，约 125Hz（8ms 一帧） | ✅ 已实现：按需开启，客户端只保留最新一帧 |
| `/sensor` | 服务器 → 客户端 | 二进制帧（`uint32 LE 头长 + JSON 头 + payload`） | ✅ 点云已实现（`SensorFrameCodec`：zstd / raw + int16 反量化），覆盖式只保留最新一帧；影像 / 超声仍未解码 |

> 常量对照：`RUSTool.Core/Communication/ProtocolConstants.cs` 的 `Channels` / `Commands` / `Events` /
> `SensorTypes` / `SensorEncodings` / `SensorScopes` 与本文逐条对齐；**改协议先改那份常量**，
> 客户端不允许出现裸字符串。

---

## 1. 状态推送 (Server → Client)

服务器每 8ms 广播一次机器人状态，格式如下：

```json
{
  "timestamp":  123456.789,
  "frame_rate": 998.0,
  "joint_pos":  [0.0, -0.5, 1.2, 0.0, 0.3, 0.0],
  "joint_vel":  [0.01, -0.02, 0.03, 0.0, 0.0, 0.0],
  "joint_acc":  [0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
  "effort":     [0.0, 0.0, 0.0, 0.0, 0.0, 0.0],
  "flange_pos": [0.3, 0.0, 0.5, 3.14, 0.0, 0.0]
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `timestamp` | double | 时间戳，秒 |
| `frame_rate` | double | 仿真帧率（真实驱动占位 125） |
| `joint_pos` | double[6] | 关节角，rad |
| `joint_vel` | double[6] | 关节速度，rad/s |
| `joint_acc` | double[6] | 关节加速度，rad/s² |
| `effort` | double[6] | 关节力矩，Nm |
| `flange_pos` | double[6] | 法兰位姿 [x,y,z,rx,ry,rz]，m/rad |

### 1.1 C# 反序列化

```csharp
public record RobotState
{
    public double Timestamp { get; init; }
    public double FrameRate { get; init; }
    public double[] JointPos { get; init; } = [];
    public double[] JointVel { get; init; } = [];
    public double[] JointAcc { get; init; } = [];
    public double[] Effort { get; init; } = [];
    public double[] FlangePos { get; init; } = [];
}
```

---

## 2. 指令发送 (Client → Server)

### 2.1 请求格式

```json
{
  "cmd":  "movej",
  "args": [0.1, -0.5, 1.2, 0.0, 0.3, 0.0],
  "id":   42
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `cmd` | string | 指令名 |
| `args` | double[] | 参数数组 |
| `id` | int | 请求 ID，用于匹配响应（可选） |

### 2.2 响应格式

```json
{
  "id":      42,
  "success": true,
  "result":  []
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `id` | int | 对应请求的 ID |
| `success` | bool | 执行成功/失败 |
| `result` | double[] | 返回值（查询类指令使用） |

---

## 3. 完整指令列表

### 3.1 运动指令

| 指令 | args 格式 | 说明 |
|------|-----------|------|
| `movej` | `[q1..q6, speed?, acc?]` | 关节空间运动 |
| `movel` | `[x, y, z, rx, ry, rz, speed?, acc?]` | 笛卡尔直线运动 |
| `servoj` | `[q1..q6]` | 关节伺服，需先 `servo_start` |
| `servo_cart` | `[x, y, z, rx, ry, rz]` | 笛卡尔伺服，需先 `servo_start` |

### 3.2 点动指令

| 指令 | args 格式 | 说明 |
|------|-----------|------|
| `start_jog` | `[ref, nb, dir, vel, acc, max_dis]` | 启动点动 |
| `stop_jog_decel` | `[]` | 减速停止 |
| `stop_jog_immediate` | `[]` | 立即停止 |

**点动参数说明：**

| 参数 | 范围 | 含义 |
|------|------|------|
| `ref` | 0=关节, 2=基坐标, 4=工具 | 点动模式 |
| `nb` | 1~6 | 轴号 |
| `dir` | 0=负, 1=正 | 方向 |
| `vel` | 0~100 | 速度百分比 |
| `acc` | 0~100 | 加速度百分比 |
| `max_dis` | ≥0 | 最大位移，0=无限 |

### 3.3 驱动控制

| 指令 | args | result | 说明 |
|------|------|--------|------|
| `connect` | `[]` | — | 连接机器人 |
| `disconnect` | `[]` | — | 断开连接 |
| `is_connected` | `[]` | `[1/0]` | 查询连接状态 |
| `robot_enable` | `[state]` | — | 上使能(1)/下使能(0) |
| `get_state` | `[flag]` | `[ts, q1..q6]` | 获取当前状态 |
| `switch_driver` | `[type]` | — | 切换驱动：`0` = 仿真（sim）/ `1` = 真实（real） |
| `get_driver_type` | `[]` | `[0/1]` | 查询当前驱动类型（编码同 `switch_driver`）；**无参** |
| `is_motion_done` | `[]` | `[1/0]` | 查询运动完成 |
| `is_in_drag_teach` | `[]` | `[1/0]` | 是否拖动示教 |

**驱动类型只有后端说了算。** `switch_driver` 的回执只表示「后端收到了请求」，
不代表当前驱动已经切过去了；界面上的高亮一律以 `get_driver_type` 的回读值为准 ——
**连接成功 / 断线重连成功 / 切换驱动之后都要回读一次**（后端可能被外部改过驱动，重连之后
也不一定是原来那个）。回读失败或还没连上时，界面把驱动按钮整体变灰并显示「未知」，
绝不拿旧值继续显示。

驱动编码只有一份，前后端共用：`0` = 仿真（sim）、`1` = 真实（real）。
C# 侧的转换只在 `RobotDriverCodec` 一处（见 [`../core/zh-CN.md`](../core/zh-CN.md) 第 8 节），
界面拿到的一律是 `RobotDriver` 枚举，不做二次映射。

### 3.4 运动控制

| 指令 | args | 说明 |
|------|------|------|
| `stop` | `[]` | 停止所有运动 |
| `pause` | `[]` | 暂停 |
| `resume` | `[]` | 恢复 |

### 3.5 伺服模式

| 指令 | args | 说明 |
|------|------|------|
| `servo_start` | `[]` | 开启伺服模式 |
| `servo_end` | `[]` | 结束伺服模式 |

### 3.6 仿真控制（仅 Sim 驱动）

| 指令 | args | result | 说明 |
|------|------|--------|------|
| `set_time_speed` | `[speed]` | — | 设置仿真倍速 |
| `get_time_speed` | `[]` | `[speed]` | 查询仿真倍速 |
| `get_sim_time` | `[]` | `[time]` | 查询仿真时间 |
| `get_frame_rate` | `[]` | `[fps]` | 查询仿真帧率 |
| `step_once` | `[]` | — | 单步仿真 |
| `is_playback_active` | `[]` | `[1/0]` | 是否正在回放 |

### 3.7 录制/回放

| 指令 | args | 说明 |
|------|------|------|
| `record_start` | `[]` | 开始录制 |
| `record_stop` | `[]` | 停止录制 |
| `playback_start` | `[]` | 开始回放 |
| `playback_stop` | `[]` | 停止回放 |

### 3.8 文件执行

| 指令 | args | 说明 |
|------|------|------|
| `run_file` | `[]` | 执行指令脚本文件 |

---

## 4. C# 客户端实现示例（最小对照）

> 本节的示例**只是协议的最小对照代码**（单连接、无重连策略）。仓库里真正在跑的实现是
> `RUSTool.Core/Communication/` 下的三个类（`BridgeClient` / `ConnectionManager` / `BridgeProtocol`）——
> 两者字段解析必须一致，行为以 [`../core/zh-CN.md`](../core/zh-CN.md) 为准。

### 4.1 WebSocket 连接

```csharp
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

public class RobotMonitorClient : IAsyncDisposable
{
    private ClientWebSocket _ws = new();
    private CancellationTokenSource _cts = new();
    private int _requestId;

    public event Action<RobotState>? OnStateUpdated;

    public async Task ConnectAsync(string url = "ws://localhost:8765")
    {
        _ws = new ClientWebSocket();
        await _ws.ConnectAsync(new Uri(url), _cts.Token);
        _ = Task.Run(ReceiveLoopAsync);
    }

    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[65536];
        while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
        {
            var result = await _ws.ReceiveAsync(buffer, _cts.Token);
            if (result.MessageType == WebSocketMessageType.Text)
            {
                var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                HandleMessage(json);
            }
        }
    }

    private void HandleMessage(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 有 "cmd" 字段的是状态推送，否则是指令响应
        if (root.TryGetProperty("cmd", out _))
        {
            // 指令响应
            var id = root.GetProperty("id").GetInt32();
            var success = root.GetProperty("success").GetBoolean();
            // ... 回调处理
        }
        else
        {
            // 状态推送
            var state = JsonSerializer.Deserialize<RobotState>(json);
            if (state != null)
                OnStateUpdated?.Invoke(state);
        }
    }

    public async Task SendCommand(string cmd, double[] args)
    {
        var id = Interlocked.Increment(ref _requestId);
        var request = new { cmd, args, id };
        var json = JsonSerializer.Serialize(request);
        var bytes = Encoding.UTF8.GetBytes(json);
        await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_ws.State == WebSocketState.Open)
            await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", default);
        _ws.Dispose();
    }
}
```

### 4.2 断线重连

```csharp
public async Task RunWithReconnect(string url = "ws://localhost:8765")
{
    while (true)
    {
        try
        {
            await ConnectAsync(url);
            // 连接成功，等待断开
            await Task.Delay(-1, _cts.Token);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"连接断开: {ex.Message}");
            Console.WriteLine("5 秒后重连...");
            await Task.Delay(5000);
        }
    }
}
```

### 4.3 MVVM 集成

```csharp
public partial class MainViewModel : ObservableObject
{
    private readonly RobotMonitorClient _client = new();

    [ObservableProperty] private double[] _jointPositions = [];
    [ObservableProperty] private double _frameRate;

    public MainViewModel()
    {
        _client.OnStateUpdated += OnState;
    }

    private void OnState(RobotState state)
    {
        // 调度到 UI 线程
        App.Current.Dispatcher.Post(() =>
        {
            JointPositions = state.JointPos;
            FrameRate = state.FrameRate;
        });
    }

    public async Task Connect() => await _client.ConnectAsync();
    public Task Jog(int axis, int dir) => _client.SendCommand("start_jog", [0, axis, dir, 30, 30, 0]);
    public Task StopJog() => _client.SendCommand("stop_jog_decel", []);
}
```

---

## 5. 感知流（/sensor）：点云二进制帧的解码契约

与 JSON 通路完全分开：**一条 WS 二进制消息 = 一帧**。客户端实现见
`RUSTool.Core/Communication/SensorFrameCodec.cs`（纯函数，无状态，可单测）。

| 项 | 值 |
|---|---|
| 帧格式 | `uint32 LE 头长度` + `JSON 头`（UTF-8）+ `payload` |
| 完整性自检 | `4 + 头长度 + payload 长度 == 消息长度`，不成立 → 整帧丢 |
| payload 压缩 | 头字段 `encoding`：`zstd`（后端当前固定）/ `raw`；**未知值整帧丢，不猜** |
| 解压后布局 | 每点 10 字节：`int16 x` \| `int16 y` \| `int16 z` \| `uint32 rgb`，全小端 |
| 反量化 | `v = min + (q + 32768) × (max − min) / 65535`，`min/max` 取【本帧头里的】`range_min` / `range_max` |
| 量化（客户端只用于造帧 / 单测） | `q = round((v − min) × 65535 / (max − min)) − 32768`，夹进 `int16` |
| 颜色 | `0x00RRGGBB` 打包整数（`(byte)(c >> 16)` 取红），**不是** PCL 的 float 位模式 |
| 坐标系 | `frame_id = base_link`：后端已算好变换，客户端**不做**任何坐标变换 |
| 每帧语义 | 自包含的完整点集，**一律整帧替换**；协议里没有 delta / 差分字段 |
| `scope` | `frame` = 单视角当前帧（10 Hz）；`map` = 累积地图快照（0.5 Hz，点数大得多）—— 两者走同一条整帧替换路径，`scope` 只用来标注读数与预期点数 |
| 未实现的一律丢 | `type = image` / `compressed`、`dtype ≠ int16`、`fields ≠ [x,y,z,rgb]`、`encoding` 未知 |
| 点数守卫 | 头里 `points > 4 000 000` → 整帧丢（否则会先分配几百 MB 的数组） |
| 丢帧语义 | 覆盖式（bridge 只留最新一帧），`seq` 跳跃即丢帧；⚠️ `map_clear` 会把 `seq` 复位为 0，**不能**用单调递增做断言 |
| 首帧 | 连接成功后 bridge 立刻推送缓存的最新一帧，不必等下一次发布 |

解码产出的形状是 `SensorPointCloudFrame`：`float[] Xyz`（米，交错）+ `uint[] Rgb`（每点一个打包颜色）。

**踩坑清单**（与引擎侧 `docs/Protocol/WsProtocol.md` §3.3 一一对应）：

1. **必须循环收到 `EndOfMessage`**：兆级帧必然被 TCP / WS 分片，绝不能按「收到一次数据 = 一帧」写
   （本仓库在 `ConnectionManager.ReceiveFrameAsync` 里拼帧）。
2. **全小端**：统一 `BinaryPrimitives.Read*LittleEndian`，不要用 `BitConverter` 的本机端序。
3. **`encoding` 要分支**：`zstd` 与 `raw` 都是协议允许的值，写死一个等于赌后端实现。
4. **`range_min/max` 逐帧变化**：只能用本帧头里的值，禁止缓存复用（后端对退化包围盒补了 1 mm，不会除零）。
5. **解码不在 UI 线程**：30 万点解压 + 反量化放在 WebSocket 线程（`BridgeClient.OnSensorMessage`），
   渲染侧只保留最新一帧 —— 处理慢了就丢帧，而不是积压。
6. **坏帧只记不抛**：覆盖式通道下坏帧可能每帧都来，客户端只记第 1 次与之后每 100 次（`BridgeClient` 的计数）。

**本客户端的完整落地路径**：

| 环节 | 位置 |
|---|---|
| 通道连接 / 分片拼帧 | `ConnectionManager.ConnectSensorAsync` · `SensorReceiveLoopAsync` |
| 解压 + 反量化 + 自检 | `SensorFrameCodec.TryDecode`（失败返回 `null` + 中文原因） |
| 覆盖式信箱 + 事件 | `BridgeClient.StartSensorStream` · `SensorFrameReceived` · `LatestSensorFrame` |
| 业务门面 | `IRobotService.StartSensorStream` / `StopSensorStream` / `SensorFrameReceived` |
| 进场景图 | 界面层适配 → `RobotViewport.SubmitPointCloud`（邮箱）→ `PointCloudLayer`（整帧替换） |

> `type = image` / `ultrasound` 的帧目前会被整帧丢弃（并记一条日志）：通道与常量都在，
> 解码待接入 —— 与 `SensorTypes` 里标注的状态一致。

---

## 6. 注意事项

1. **频率**：状态推送 125Hz（8ms 间隔），C# 端 UI 更新建议限制在 30~60fps
   （本仓库客户端只保留最新一帧，VM 侧不再做二次限流）。
2. **按通道分流**：状态帧走 `/state`、回执与事件走 `/control`、感知帧走 `/sensor`，
   因此**不需要**靠字段猜消息类型（三个接收循环各收各的通道）。
   第 4 节示例里的 `cmd` 字段判断，只是为了兼容「单连接服务器」的老写法。
3. **掉线处理**：`/control` 由客户端自动重连（退避 500ms → 10s 封顶），
   断线时所有未决请求被置为失败；`/state` 与 `/sensor` 的重连由 `BridgeClient` 负责
   （`StopStateStream` / `StopSensorStream` 之后不再重连）。
4. **地址**：仿真默认 `localhost:8765`，真实机器人运行时需确认 IP。
