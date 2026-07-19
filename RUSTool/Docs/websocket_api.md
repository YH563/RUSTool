# WebSocket 监控接口文档

## 概述

DriverNode 启动时自动在 `ws://localhost:8765` 开启 WebSocket 服务器。

- **状态推送**：服务器 → 客户端（125Hz JSON）
- **指令发送**：客户端 → 服务器（JSON 请求 → JSON 回复）

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

### C# 反序列化

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

### 请求格式

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

### 响应格式

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
| `is_motion_done` | `[]` | `[1/0]` | 查询运动完成 |
| `is_in_drag_teach` | `[]` | `[1/0]` | 是否拖动示教 |

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

## 4. C# 客户端实现示例

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

## 5. 注意事项

1. **频率**：状态推送 125Hz（8ms 间隔），C# 端 UI 更新建议限制在 30~60fps
2. **状态鉴别**：JSON 有 `joint_pos` 字段的是状态推送，有 `success` 字段的是指令响应
3. **掉线处理**：建议客户端实现自动重连（见 4.2）
4. **地址**：仿真默认 `localhost:8765`，真实机器人运行时需确认 IP
