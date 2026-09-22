# RUSTool

> 本仓库文档**只提供中文版本**：这一页是仓库入口，分模块正文全部在 [`docs/`](docs/)，索引见 [`docs/README.md`](docs/README.md)。

超声扫查机器人的**上位机**：Avalonia + MVVM 桌面应用，通过 WebSocket 连后端 bridge，
为「工程师」与「临床」两类使用者提供**两套工作区**（共享同一份业务状态，只有投影不同），
并在界面里内嵌一个**真实的 3D 机械臂视口**（Silk.NET + OpenGL）。

---

## 1. 包含的工程

仓库按「谁该认识谁」的边界组织，目录即工程边界：三个库 / 应用 +（不发布的）测试工程。

| 工程 | 类型 | 职责 | 依赖 |
|---|---|---|---|
| **`RUSTool.Core`** | 类库 | 纯逻辑层：bridge 通信客户端（`BridgeClient` / `ConnectionManager` / `BridgeProtocol` / `SensorFrameCodec`）、机器人业务服务（`IRobotService` / `RobotService` / `RobotSession`）、流程状态机（`ScanStateMachine`）、日志契约（`ILogService`）。零界面、零图形依赖 | `CommunityToolkit.Mvvm`（仅 `ObservableObject`）、`ZstdSharp.Port`（`/sensor` 点云帧解压） |
| **`RUSTool.UI`** | WinExe | **唯一的应用**（`dotnet run` 起来的就是它）：Avalonia 界面 + 设计系统 `Theme/` + 依赖图组装（唯一组合根） | `Core`、`Visualization`、Avalonia 12.1.0、CommunityToolkit.Mvvm 8.4.2、Avalonia.Headless（截图） |
| **`RUSTool.Visualization`** | 类库 | 图形栈的**隔离容器**：`RobotViewport`（内嵌 3D 视口）、`RobotScene` + `PointCloudLayer`（URDF + 关节驱动 + 感知点云）、`SimulationLogBridge`（库日志接出）。Silk.NET / OpenGL / `RobotSimulation` 只在这里出现 | `Core` 无关，依赖 `RobotSimulation` 0.3.1、Silk.NET.OpenGL 2.23.0、Avalonia 12.1.0 |
| *(test) `tests/RUSTool.Core.Tests`* | xUnit | 纯逻辑单测：`ScanStateMachine` 54 + `SensorFrameCodec` 24 = **78 个用例**，不依赖网络 / 界面 / 图形栈。**不发布** | `Core` |
| *(data) `RUSTool.Visualization/Assets/Models`* | 数据 | 随编译复制到输出目录的 URDF + mesh（首选真机模型、兜底 URDF 内置几何） | — |

依赖方向是**单向**的，由编译器强制：`RUSTool.UI` → `RUSTool.Core`、`RUSTool.UI` → `RUSTool.Visualization`。

```text
RUSTool.sln
├── RUSTool.Core/              类库 · 纯逻辑层（不得出现 Avalonia / XAML / Silk.NET / OpenGL）
│   ├── Communication/         通信客户端：BridgeClient（门面）· ConnectionManager（连接/重连）
│   │                          · BridgeProtocol（消息模型 + JSON 编解码）· SensorFrameCodec（/sensor 点云帧解码）
│   │                          · ProtocolConstants（通道/指令/事件/感知常量）
│   ├── Services/Robot/        业务层：IRobotService（契约）· RobotService（实现）· RobotSession（共享状态 + 模式仲裁）
│   │   └── Workflows/         ScanStateMachine.cs —— 扫查流程状态机（零依赖，可脱离网络 / 界面单测）
│   └── Services/Logging/      ILogService（契约；实现留在界面层）
│
├── RUSTool.UI/                WinExe · 唯一的应用项目
│   ├── Program.cs             进程入口：正常启动 / `--shot` 离屏截图（`--demo-cloud` 合成点云）
│   ├── App.axaml.cs           应用入口 + 依赖图组装（composition root，全项目唯一 new 实现处）
│   ├── Theme/                 设计系统：令牌 + 控件样式（只含 XAML 资源，不产出 C# 类型）
│   ├── Styles/AppLayout.axaml 应用级布局类（card / cardHeader / sunken / tag …）
│   ├── ViewModels/            6 个子 VM：Session / Status / Control / Scan / Log / Replay
│   ├── Views/                 MainWindow + Debug/（工程师）+ Clinical/（临床）
│   ├── Services/              Logging/（LogService · SimulationLogSink）· DemoSensorFrame（截图用的合成帧）
│   └── preview.sh             一条命令跑界面 / 拍截图
│
├── RUSTool.Visualization/     类库 · 图形栈的隔离容器
│   ├── Controls/RobotViewport.cs  内嵌 3D 视口（OpenGlControlBase：GL 生命周期 / 每帧 / 相机 / 拾取 / 两条数据邮箱）
│   ├── Scene/RobotScene.cs        场景装配（默认场景 + URDF + 关节驱动 + 点云图层；纯 CPU，可脱离 GL 检查）
│   ├── Scene/PointCloudLayer.cs   感知点云图层（整帧替换）
│   ├── Logging/                   ISimulationLogSink + SimulationLogBridge（库日志 → 项目日志器）
│   └── Assets/Models/             URDF + STL（随编译复制到输出目录）
│
├── docs/                      分模块文档（中文）：architecture / core / protocol / ui / visualization / testing
└── tests/RUSTool.Core.Tests/  单元测试（xUnit；ScanStateMachineTests 54 + SensorFrameCodecTests 24 个用例）
```

> 打包：本解决方案**不发布 NuGet 包**（`IsPackable=false`），也没有 `Directory.Build.props` ——
> 它只服务于本机与内网部署；可复用的 3D 内核是外部依赖 `RobotSimulation`。

## 2. 核心特性

- **两套工作区、一份业务状态**：共享工具栏（菜单 / 连接与运动状态灯 / `switch_driver` 驱动切换 / 急停与恢复）
  + 工程师工作区（上排 `3D 场景 / 超声影像 / 数据曲线`，下排 `机器人指令 / 回放 / 日志`）
  + 临床工作区（左侧扫查主视图 + 右侧 4 步流程向导 + 常驻急停）。业务逻辑**不复制**，只有投影不同。
- **手动控制**：6 轴点动（**按住走、松手停**，含指针被抢走时补发停止）、MoveJ / MoveL、暂停 / 恢复 / 停止 / 复位。
- **扫查流程**：① 预扫查建图 → ② 位姿选点 → ③ 路径规划 → ④ 执行扫查；
  按钮按阶段门控，完成标志有**同步回执**与**异步事件**两个来源（`pre_scan_done` / `plan_done` / `motion_done`）。
- **流程内核可测**：`ScanStateMachine` 把「预扫查 → 位姿 → 规划 → 执行」压成 8 个阶段 + 转移表，
  零依赖、54 个用例锁定（含「非法动作必须被拒绝」「按钮灰不灰与能否执行一致」）。
- **真实 3D 可视化**：URDF 模型 + 关节角实时驱动 + 相机轨道操作 + 单击拾取 + 库自带朝向 gizmo；
  `JointValues` 用一个「邮箱」跨线程交给渲染线程，界面线程绝不碰场景对象。
- **感知点云**：`/sensor` 二进制帧在 WebSocket 线程解压 + 反量化（`SensorFrameCodec`），
  经视口的第二个邮箱整帧替换进场景图；覆盖式只留最新一帧，慢渲染丢帧而不是积压。
  没接后端时可用 `preview.sh window --demo-cloud` 看一眼这条链路。
- **图形栈彻底隔离**：Silk.NET / OpenGL / `RobotSimulation` 只出现在 `RUSTool.Visualization`，
  界面只认识三个契约（`RobotViewport` 控件、`PointCloudFrame` 点云帧的形状、`ISimulationLogSink` 日志出口）；
  拿不到桌面 GL 时**降级不崩**。
- **设计系统与应用分工**：配色全走 `Theme/` 的语义类，全项目**没有一个值转换器** ——
  状态灯是「一组互斥布尔量叠在同一个 `Ellipse` 上」，换肤只改 `Theme/Tokens/Semantic.axaml`。
- **一份日志**：后端指令回执、业务动作、3D 图形栈（来源列 `sim`）与界面诊断汇进同一个面板，
  限长 500 条并按天落盘。
- **预览即运行时**：离屏截图与真实启动**共用同一份组装**，`preview/*.png` 画的就是运行时那个界面。

---

## 3. 环境要求

| 项 | 要求 | 说明 |
|---|---|---|
| 目标框架 | `net8.0` | 四个工程统一（跟随 `RobotSimulation` 0.3.1 的 lib 目录） |
| **构建 SDK** | **.NET SDK 10** | 必须。Avalonia 12.1.0 的源生成器要求 Roslyn 4.14+；用 SDK 8 会加载不上源生成器，`InitializeComponent` 不被生成 → 整片 `CS0103` |
| `global.json` | **刻意不放** | 钉了 SDK 版本反而编译不过 |
| 3D | 桌面 OpenGL 3.3 core | 后端只带 `#version 330 core` 着色器；遇到 GLES 会抛 `NotSupportedException` 并被捕获 → 3D 区降级为空状态 |
| 平台 | Linux / Windows 桌面 | 无显卡 / 无 GL 时应用照常可用（HUD、指令、日志都不依赖 GL） |
| 后端 | 任何实现 bridge 协议的服务 | 默认 `ws://127.0.0.1:8765`（协议见 [`docs/protocol/zh-CN.md`](docs/protocol/zh-CN.md)） |
| NuGet 源 | **nuget.org 一个**（仓库根 `NuGet.config` 里 `<clear />` 后显式登记） | `RobotSimulation` 0.1.0 / 0.2.0 / 0.2.1 / 0.3.0 / 0.3.1 都已发布在 nuget.org 上；换机器 / 上 CI 不需要任何手工加源，也不依赖本机离线目录 |

```bash
# 本机 dotnet 装在 ~/.dotnet 但没进 PATH（preview.sh 会自己设好）
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$PATH:$DOTNET_ROOT"
dotnet --version        # 期望 10.x
```

## 4. 快速开始

### 4.1 构建与测试

```bash
dotnet build RUSTool.sln                 # 构建整个解决方案
dotnet test  tests/RUSTool.Core.Tests    # 78 个纯逻辑用例（54 状态机 + 24 点云帧解码），不依赖网络 / 界面 / GL
```

### 4.2 起真实窗口

```bash
RUSTool.UI/preview.sh window             # 工程师模式（可交互）
RUSTool.UI/preview.sh clinical           # 临床模式
RUSTool.UI/preview.sh window --clinical  # 参数透传（与上一行等价）
RUSTool.UI/preview.sh window --demo-cloud # 没有后端也能看：投一帧合成点云到 3D 视口
```

界面上的读数、日志、状态**全部来自真实后端**：没连上 bridge 时工具栏显示「未连接 / 空闲」、
HUD 读数为 0、日志为空 —— 这是正确行为，不是坏了。起来后点「连接」即可。

### 4.3 拍界面截图（不需要后端）

```bash
RUSTool.UI/preview.sh all                # 六张一次拍全 -> RUSTool.UI/preview/
RUSTool.UI/preview.sh dark               # 只拍「工程师模式 · 深色」
RUSTool.UI/preview.sh cloud              # 合成点云那张（3D 区在离屏下没有 GL，看日志）
RUSTool.UI/preview.sh popup MenuFile     # 展开菜单后截图（菜单是 Popup，不展开拍不到）
```

`preview.sh` 只是个 bash 包装，替你做了三件事：设 `DOTNET_ROOT`、把工作目录固定到仓库根、
建好 `RUSTool.UI/preview/`。**Windows 或已把 `dotnet` 加进 PATH 的机器**不需要它，直接敲等价命令：

```bash
mkdir -p RUSTool.UI/preview              # 脚本会替你建；手敲要先建，--shot 不会创建目录
dotnet run --project RUSTool.UI -- --shot RUSTool.UI/preview/02-engineer-dark.png --dark
dotnet run --project RUSTool.UI -- --clinical          # 真实窗口 · 临床模式
dotnet run --project RUSTool.UI -- --shot RUSTool.UI/preview/05-menu-MenuFile.png --open MenuFile
```

`--shot` 的路径按**当前工作目录**解析，所以在仓库根执行（绝对路径同样可以）。

离屏渲染拿不到桌面 GL，所以截图里的 3D 区是**设计好的空状态** —— 属预期降级；
要看真实 3D 请用 `preview.sh window`（stderr 会打印 `[3d] 就绪 …`，含 GPU 与模型加载报告）。

### 4.4 接后端 bridge

```
后端 bridge  ──ws://127.0.0.1:8765──►  /control（指令 / 回执 / 事件，必连，自动重连）
                                        /state   （状态帧，约 125Hz，只保留最新一帧）
                                        /sensor  （二进制感知帧，点云已解码 → 3D 视口）
```

客户端已按协议实现（`RUSTool.Core/Communication/`），后端联调时对照
[`docs/protocol/zh-CN.md`](docs/protocol/zh-CN.md)：状态帧字段、请求 / 回执格式、完整指令表。
单位约定：**弧度 / 米 / Z 轴向上**。

---

## 5. 分模块文档

全部拆成模块文档，只有中文版；索引见 [`docs/README.md`](docs/README.md)。

| 文档 | 内容 |
|---|---|
| [`docs/architecture/zh-CN.md`](docs/architecture/zh-CN.md) | 工程边界、依赖方向、分层职责、关键设计决策（ADR）、扩展指南、演进进度 |
| [`docs/core/zh-CN.md`](docs/core/zh-CN.md) | `RUSTool.Core` 公共面：通信客户端、消息模型、`/sensor` 感知帧解码、业务服务、共享会话、流程状态机、日志契约、反例 |
| [`docs/protocol/zh-CN.md`](docs/protocol/zh-CN.md) | bridge 协议：三条通道、状态帧、请求 / 回执、完整指令表、`/sensor` 点云帧解码契约、注意事项 |
| [`docs/ui/zh-CN.md`](docs/ui/zh-CN.md) | `RUSTool.UI`：两类使用者的心智模型、指令分层、两个工作区、主题、组装根与数据流、已知边界 |
| [`docs/visualization/zh-CN.md`](docs/visualization/zh-CN.md) | `RUSTool.Visualization`：数据契约（`RobotViewport`）、感知点云图层与线程交接、日志出口、依赖、资产、反例 |
| [`docs/testing/zh-CN.md`](docs/testing/zh-CN.md) | 构建 SDK 要求、单元测试覆盖、界面 / 3D 验证、手工自检清单、常见问题 |
| [`RUSTool.UI/Theme/README.md`](RUSTool.UI/Theme/README.md) | 设计系统用法：令牌四层结构、语义类、控件样式、弹层圆角 |
| [`RUSTool.Visualization/Assets/Models/README.md`](RUSTool.Visualization/Assets/Models/README.md) | 3D 模型资产的布局规则与加载顺序 |

## 6. 约定与约定俗成

| 约定 | 说明 |
|---|---|
| **坐标与单位** | 弧度 / 米 / Z 轴向上（与 `RobotSimulation` 一致）；只有 HUD 显示度数 |
| **依赖方向** | `UI → Core`、`UI → Visualization`，单向且编译期强制；Core 永不引用界面与图形栈 |
| **协议字符串只在一层** | 只出现在 `RobotService`；VM 里不写 `"movej"` / `"plan"` |
| **唯一组装点** | `App.axaml.cs` 的 `CreateMainViewModel` 是全项目唯一 `new` 具体实现的地方；换后端只改一处 |
| **VM 只报状态，不报颜色** | 全项目**没有一个值转换器**；「状态 → 颜色」走主题语义类（互斥布尔量叠在同一个控件上） |
| **配色只改一处** | 换肤改 `Theme/Tokens/Semantic.axaml`；布局类在 `Styles/AppLayout.axaml`，两者互不牵就 |
| **状态变化只有一个入口** | 流程走 `ScanStateMachine.TryFire()`（`CanFire()` 与它判定同源）；操作模式走 `RobotSession.TryEnter*` / `ExitToIdle`；连接类动作只从 `SessionViewModel` 发 |
| **回执与事件谁先到都算数** | 长任务看后端事件、短指令看回执；重复到达是显式写出的无害自转移 |
| **日志只有一份** | `LogService` 持有集合（限长 500 + 按天落盘），3D 图形栈日志经 `SimulationLogBridge.Attach` 汇入（来源列 `sim`），且必须在建视口**之前**挂 |
| **点动是「按住走、松手停」** | 指针按下发 `start_jog`、松开发 `stop_jog_decel`；被系统抢走（`PointerCaptureLost`）也要补发停止 |
| **截图与启动同源** | `--shot` 复用 `CreateMainViewModel`，预览的界面就是运行时界面 |
| **失败一律降级** | 缺模型 / 缺桌面 GL / GLES 上下文都不许白屏与崩溃，只报告原因 |
| **场景图只有渲染线程能写** | 关节值与点云帧都只写视口的「一格邮箱」；渲染回调取走即置空（点云是覆盖式：没画完就被顶掉的那帧计入丢帧数） |

---

## 7. 路线图

- [x] 工程拆分：`RUSTool.Core`（纯逻辑）、`RUSTool.UI`（唯一应用 + 设计系统）、`RUSTool.Visualization`（图形栈隔离容器）；
      原 `RUSTool/`、`RUSTool.Theme`、`tools/`（主题画廊与演示副本）已删除 ✔
- [x] 业务接线：`BridgeClient` / `IRobotService` / `RobotSession` / `ILogService` 全部在 `App.CreateMainViewModel` 组装 ✔
- [x] 两套工作区 + 共享工具栏（驱动切换 / 急停）+ 阶段门控的四步扫查流程 ✔
- [x] 真实 3D 视口：URDF 模型 + 关节驱动 + 相机 + 拾取 + 库自带朝向 gizmo，无 GL 时降级 ✔
- [x] `/sensor` 点云通道：二进制帧解码（zstd / raw + int16 反量化，坏帧一律丢）+ 覆盖式邮箱 + 3D 视口整帧替换 ✔
      测试 **24 个用例**；没接后端时 `preview.sh window --demo-cloud` 可演一遍 ✔
- [x] 单元测试：`ScanStateMachine` **54 个用例**（含穷举式的「按钮灰不灰 = 能否执行」）✔
- [ ] **状态机接线**：`ScanWorkflowViewModel` 改为驱动 `ScanStateMachine`；`RobotSession.TryEnter*` 接入手动 / 扫查模式仲裁
- [ ] **点云选点**：3D 视口里点击点云表面取点（raycast / 最近点）→ `set_start_pose` / `set_end_pose` 带坐标
- [ ] **测试补齐**：`BridgeProtocol`（样例 JSON / 字段缺省 / 坏 JSON）、`BridgeClient`（id 匹配 / 超时 / 断线置失败）
- [ ] **回放模块**：时间轴、A/B 循环、超声影像与曲线的双轨联动（当前是演示数据）
- [ ] `/sensor` 的 `image` / `ultrasound` 帧解码（当前整帧丢弃并记日志）
- [ ] 影像 / 数据曲线的真实数据源接入
- [ ] （可选）后端 bridge 的本地联调脚本 / 集成测试

## 8. 已知边界与常见问题

| 现象 / 边界 | 说明 |
|---|---|
| 界面读数为 0、日志为空 | 没连上 bridge 的**离线态**，属正确行为；`./preview.sh window` 起来后点「连接」即可 |
| 3D 区是空的（显示主题化空状态） | 拿不到桌面 OpenGL（离屏截图 / 无显卡机器 / GLES 上下文）时控件自己隐藏并走 `Failed`，不影响其余功能 |
| 3D 里没有点云 | 点云走 `/sensor`，点「连接」即开该通道；**没接后端**时用 `preview.sh window --demo-cloud` 显式投一帧合成点云 |
| 截图里看不到下拉菜单 | 菜单栏下拉是 Popup，离屏会被托管到 OverlayLayer；用 `./preview.sh popup <菜单名>` 在真实窗口里拍 |
| 「末端接触力」是近似值 | 状态帧里没有独立的接触力通道，目前用各关节力矩模和代替；后端一旦提供 `contact_force` 字段，只改 `RobotStatusViewModel.OnStateUpdated` 一处 |
| 影像 / 曲线 / 回放是占位 | 曲线是两条装饰性正弦（红 Fx、绿 Fy），不是真实力信号；回放时间轴也是演示数据 |
| 「急停」没有独立的后端通道 | 后端只回执 `stop`，所以急停是否按下是本地界面状态（`SessionViewModel.IsEmergencyStopped`） |
| 状态行一直显示「空闲 / 已暂停」 | 模式仲裁（`RobotSession.TryEnter*`）尚未接线，属已知缺口（见路线图） |
| 深色弹层圆角为 0 | 有意为之：没有合成器时透明区会被渲染成黑色；确认有合成器后可改 `Theme` 的 `RadiusOverlay` / `ShadowOverlay` |
| 日志会落盘 | `LogService` 默认写 `logs/`（相对**进程工作目录**），已在 `.gitignore` 里忽略 |
| 构建报 `CS0103` 一大片 | 用了 .NET SDK 8 构建。Avalonia 12 的源生成器需要 Roslyn 4.14+，请换 SDK 10 |
| 构建报某文件被占用 | 同一份输出目录被并发的 `build` / `test` / `run` 同时写；串行执行即可 |

更多排查项见 [`docs/testing/zh-CN.md`](docs/testing/zh-CN.md) 第 6 节。

---

## 9. 许可

- 本仓库当前**未包含** `LICENSE` 文件 —— 对外发布前需要先补一份许可声明（由作者决定采用哪种许可）。
- 3D 内核 `RobotSimulation`（Core / Robot / OpenGL，0.3.1）为自研库；
  `RUSTool.Visualization/Assets/Models/` 下的测试模型与 mesh 随该库仓库分发，许可以那一部分为准
  （该库采用 MIT；本目录不再单独声明）。
- 本仓库自有源码的许可随仓库根声明；在上面那条补齐之前，请按「内部项目」对待。