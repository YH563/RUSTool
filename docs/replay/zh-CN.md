# RUSTool.Replay（本地回放）与 RUSTool.Settings（全局参数）

> 回放职责已由后端移交前端：后端只把两条流录成 `<records_dir>/*.rusrec`，
> **前端直接读该目录**自行解码 / 播放 / 可视化，不再经 ROS 话题（不会与在线驱动撞话题）。
> 录制仍走后端 `recorder_*`（本文不重复，见 `../protocol/zh-CN.md` §3.7）。

## 1. 工程边界

| 工程 | 职责 | 依赖 | 不引用 |
|---|---|---|---|
| **`RUSTool.Replay`** | `.rusrec` 容器读取 + ROS CDR 解码 + 本节目录发现 + 回放引擎 | `Core`（借 `StateFrame` / `SensorPointCloudFrame` / `SensorFrameCodec` / `ILogService`） | Avalonia / 图形栈 / 界面 VM |
| **`RUSTool.Settings`** | 全局参数模型 + JSON 持久化 + 变更通知 | `CommunityToolkit.Mvvm` | 其它业务工程 |

`RUSTool.UI` 引用两者；`ReplayViewModel` 是**薄适配层**（把引擎状态/事件翻译成可绑定属性），
`SettingsWindow` / 回放条直接绑定 `SettingsService`。

## 2. 文件格式与解码（`RUSTool.Replay`）

- `RusRec.cs`：`.rusrec` v1 容器（FileHeader → 通道表 → 记录 → 尾索引 → Footer）。
  记录用**顺序扫描**建立（每条只读 40B 头、payload seek 跳过），正常关闭与崩溃/截断文件同一路径；
  `ReadPayload` 校验 payload CRC32。通道约定：`0=/driver/state`、`1=/sensor/pointcloud`。
- `CdrReader.cs` + `RecPayloadDecoder.cs`：payload 是 ROS 消息的 CDR 字节，映射到前端两个契约
  —— `RobotState → BridgeProtocol.StateFrame`；`SensorFrame → SensorPointCloudFrame`
  （复用 `SensorFrameCodec` 的反量化，和实时 `/sensor` 同一条路径）。
  `RobotState` 解码**带 `tool_index` / `tool_pose`**（不能丢：丢了回放时 `Status.TcpPose` 为空，
  3D 里的工具坐标系会消失，而实时链路 `/state` 是带 `tool_pose` 的）。
- `RecordingsLibrary.cs`：列出 `*.rusrec`（文件名升序 = 时间序）。

## 3. 回放引擎（`LocalReplayPlayer`）

- 时间轴：有效时间戳（消息时间戳，缺失退回入队时刻），首条平移 0；回退钳到前一条。
- 控制：播放 / 暂停 / 停止 / seek / 快退快进 / 单步 / 倍速（0.05~20）。
- 后台 `Timer` 推进，**解码在后台线程**（含点云 zstd）；事件 `StateFramePlayed` /
  `PointCloudPlayed` / `Changed` / `Error`，订阅方负责 marshal 回自己的线程。
- 坏帧 / CRC 失败 → 停止回放 + 抛 `Error`，绝不继续发坏数据。

## 4. 进入回放要"接管"（断后端）

回放是本地独占：播放前 `MainViewModel.RequestReplayTakeoverAsync` 按序执行 ——

1. **运动门控**：`Control.IsJogging || Scan.IsRunning || Session.IsScanning` → 拒绝进入（提示先停止）；
2. **兜底停止**：下发 `stop` 并等回执，失败则中止（绝不带着未知运动状态断开）；
3. **断开后端**：停 `/state`、`/sensor` 流并 disconnect。

之后本地回放独占 HUD / 曲线 / 3D；退出回放后后端保持断开，由用户手动重连。

## 5. 全局参数（`RUSTool.Settings`）

- `AppSettings`：`RecordsDirectory`（录音目录）、`BridgeHost` / `BridgePort`、`LastReplayFile`，均带默认值。
- `JsonSettingsStore`：落盘到 `~/.config/RUSTool/settings.json`（Windows 为 `%APPDATA%`）；
  读取损坏回退默认值、保存失败静默返回 false，绝不让应用因一个坏 JSON 起不来。
- `SettingsService`：界面绑定对象，改动即落盘；端口改动下次启动生效（`BridgeClient` 启动时取值）。
- UI：菜单「设置 → 全局参数…」打开 `SettingsWindow`；回放条里也有可编辑的录音目录 + 「选择目录」。

## 6. 反例

- **不要在别处再读 `.rusrec`**：格式解析只在 `RUSTool.Replay`，重复实现迟早对不上字段。
- **不要在 UI 线程解压点云**：引擎已把解码放在后台线程；界面只接结果。
- **不要沿用后端 `replay_*`**：后端回放已废弃（`[Obsolete]`，仅过渡保留）。
- **不要硬编码录音目录 / bridge 地址**：一律走 `RUSTool.Settings`。
