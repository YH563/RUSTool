# RUSTool — 项目架构

## 解决方案结构

依赖方向**单向**，由编译器强制：`RUSTool.UI`（界面）→ `RUSTool.Core`（逻辑）。

```
RUSTool.sln
│
├── RUSTool.Core/            ← 类库 · 纯逻辑层（不得引用 Avalonia / XAML / 图形栈）
│   ├── Communication/       ← 前端通信客户端（连 bridge 的那一层）
│   │                          · BridgeClient.cs        — 对外门面（唯一公共入口，供 UI/VM 调用）
│   │                          · ConnectionManager.cs   — 连接建立 / 收发循环 / 断线重连 / 退避
│   │                          · BridgeProtocol.cs      — 消息模型 + JSON 编解码（纯函数）
│   │                          · ProtocolConstants.cs   — Channels / Commands / Events 常量
│   └── Services/
│       ├── Robot/           ← 机器人业务层
│       │   │                  · IRobotService.cs       — 业务契约（上层只依赖此接口）
│       │   │                  · RobotService.cs        — 实现：协议字符串只在这一层出现
│       │   │                  · RobotSession.cs        — 全局共享会话状态 + 模式互斥仲裁
│       │   └── Workflows/   ← 流程编排层：把无状态的指令串成有顺序的流程（见下）
│       │                      · ScanStateMachine.cs    — 扫查流程状态机（纯逻辑、零依赖、可单测）
│       └── Logging/
│           └── ILogService.cs  — 日志契约（实现留在界面层，见下）
│
├── RUSTool.UI/               ← WinExe · 唯一的应用项目（Avalonia MVVM）
│   ├── App.axaml(.cs)        ← 应用入口 + 依赖图组装（composition root，全项目唯一 new 实现处）
│   ├── Program.cs            ← 启动 + 离屏截图模式（--shot / --clinical / --dark）
│   ├── Services/Logging/
│   │   ├── LogService.cs     — ILogService 的 Avalonia 实现（Dispatcher marshal + 落盘）
│   │   └── SimulationLogSink.cs — 把图形栈的日志转手写进 LogService（实现 ISimulationLogSink）
│   ├── Theme/                ← 设计系统（原 RUSTool.Theme 工程已并入；只含 XAML，不产出 C# 类型）
│   │   ├── Theme.axaml       — 唯一入口（必须排在 FluentTheme 之后加载）
│   │   ├── Tokens/           — Palette / Semantic / Metrics / Typography
│   │   ├── Bridges/Fluent.axaml — 把 Fluent 的资源键重定向到语义色
│   │   ├── Controls/         — 控件样式（按钮变体、文字类、菜单、浮层）
│   │   └── README.md         — 设计系统的用法说明
│   ├── ViewModels/           ← 视图模型：只报状态，不报颜色（无转换器）
│   ├── Views/                ← MainWindow + Debug/（工程师）+ Clinical/（临床）
│   ├── Styles/AppLayout.axaml ← 应用级布局类（card / cardHeader / tag …）
│   ├── Assets/               ← 静态资源（应用图标）
│   ├── Docs/                 ← 协议与设计文档（websocket_api / UI / avalonia_client_design）
│   ├── app.manifest          ← 应用程序清单
│   └── preview.sh            ← 一条命令跑界面 / 拍截图
│
├── RUSTool.Visualization/    ← 类库 · 图形栈的隔离容器（Silk.NET / OpenGL 只在这里出现）
│   ├── Controls/
│   │   └── RobotViewport.cs  — 内嵌 3D 视口（OpenGlControlBase）：GL 生命周期 + 每帧 + 相机 / 拾取
│   ├── Scene/
│   │   └── RobotScene.cs     — 场景装配（默认场景 + URDF 模型 + 关节驱动）：纯 CPU，可脱离 GL 检查
│   ├── Logging/
│   │   ├── ISimulationLogSink.cs  — 日志出口契约（一个方法 + 四档枚举）：界面层只需实现它
│   │   └── SimulationLogBridge.cs — 库的 ILoggerProvider → ISimulationLogSink 的桥（等级映射 / 异常展开）
│   ├── Assets/Models/        — URDF + mesh（随编译复制到输出目录；见该目录 README）
│   └── README.md             — 图形栈的边界与数据契约（界面层只需要看这一页）
│
└── tests/RUSTool.Core.Tests/ ← 单元测试（xUnit · 不依赖网络 / 界面 / 图形栈）
    └── ScanStateMachineTests.cs
```

## 项目边界规则

| 规则 | 内容 |
|---|---|
| **依赖单向** | `RUSTool.UI` → `RUSTool.Core`；Core 不得反向引用界面层 |
| **Core 不认识 UI** | `RUSTool.Core` 内不得出现 `Avalonia.*`、XAML、窗口/控件类型；写了就编译不过 |
| **接口在 Core，实现在界面层** | 范例：`ILogService` 在 Core，`LogService` 在界面层（需 `Dispatcher.UIThread`） |
| **命名空间不随项目名变** | `RUSTool.Core` 内的类型命名空间仍是 `RUSTool.Communication.*` / `RUSTool.Services.*`，与拆分前完全一致，故调用方 `using` 无需改动 |
| **设计系统不单独成工程** | 令牌与控件样式放在 `RUSTool.UI/Theme/`，只含 XAML 资源；不产出 C# 类型，也不依赖任何业务代码 |
| **图形栈只在一个工程里** | `Silk.NET` / `OpenGL` / `RobotSimulation` 只出现在 `RUSTool.Visualization`；`RUSTool.UI` 与 `RUSTool.Core` 都不得引用它们 |
| **界面只认识两个图形契约** | `RUSTool.UI` 允许出现的图形类型只有 `RobotViewport`（控件；数据入口是纯 `float` 列表）与 `ISimulationLogSink`（日志出口）—— 图形栈换实现（或再换一个引擎）界面代码不用改 |

## 分层调用关系

```
View  ← 绑定 →  ViewModel                    ┐
                      ↓                      │ RUSTool.UI（界面层）
              Styles / 主题语义类             ┘
                      │
          ┌───────────┴───────────┐
          ▼                       ▼
     IRobotService            ILogService    ┐
          ↓                       ↑          │ RUSTool.Core（逻辑层）
     RobotService            (契约)          │
          ↓                                  │
     BridgeClient  →  ConnectionManager      ┘
                      ↓
           后端 bridge (WebSocket：/control /state /sensor)
                      ▲
                      │
        LogService（界面层实现：Dispatcher marshal）
```

## 各层职责

| 层 | 所属项目 | 职责 | 示例 |
|---|---|---|---|
| **Communication** | `RUSTool.Core` | 与 bridge 通信：指令下发/回执匹配、状态流接收、断线重连 | `BridgeClient`、`ConnectionManager`、`BridgeProtocol` |
| **Services.Robot** | `RUSTool.Core` | 机器人业务：类型化指令、共享会话状态与模式互斥仲裁 | `IRobotService`、`RobotService`、`RobotSession` |
| **Services.Robot.Workflows** | `RUSTool.Core` | 流程编排：把无状态指令串成有顺序的流程；状态机只描述「状态怎么变」，不负责发命令 | `ScanStateMachine` |
| **Services.Logging（契约）** | `RUSTool.Core` | 日志抽象 | `ILogService` |
| **Services.Logging（实现）** | `RUSTool.UI` | Avalonia 实现：集合更新 marshal 到 UI 线程 + 落文件；也是图形栈日志的落点 | `LogService`、`SimulationLogSink` |
| **ViewModels** | `RUSTool.UI` | UI 状态与命令；只依赖 Core 的接口 | `MainViewModel` 及各功能 VM |
| **Views** | `RUSTool.UI` | 界面呈现（配色走主题语义类，无值转换器） | AXAML 文件 |
| **Styles** | `RUSTool.UI` | 应用级布局类 | `AppLayout.axaml` |
| **Composition Root** | `RUSTool.UI` | 依赖图组装：全项目唯一 new 具体实现的地方 | `App.CreateMainViewModel` |
| **Visualization** | `RUSTool.Visualization` | 3D 场景与渲染：把图形栈（Silk.NET / OpenGL / RobotSimulation）关在一个工程里，对界面只暴露两个契约（控件 + 日志出口） | `RobotViewport`（GL 生命周期 + 每帧 + 相机 / 拾取）、`RobotScene`（URDF 模型 + 关节驱动）、`SimulationLogBridge`（库日志接出来） |
| **Data** | ⬜ 待拆为独立项目 | 数据库/持久化 | SQLite、PostgreSQL 仓储实现 |
| **Infrastructure** | ⬜ 待拆为独立项目 | 跨切面基础设施 | 配置、IoC 容器、异常处理 |

## 3D 内嵌是怎么接的

一条原则：**GL 的获取与 framebuffer 的绑定留在控件里，数据以普通值传进来。**

```
/state（后端）→ RobotStatusViewModel.JointsRadians（弧度；界面层只做转发，不换算）
                        │ XAML 绑定 —— 编译期检查，路径写错编译不过
                        ▼
                  RobotViewport.JointValues（在 RUSTool.Visualization 里）
                        │ 邮箱：UI 线程写、渲染线程取走（取走即置空，同一帧不会重复应用）
                        ▼
                  RobotScene.ApplyJointValues → URDF 关节的 Transform
                        │ 渲染线程：update → clear → render → 请求下一帧
                        ▼
                  控件自己的 framebuffer（Avalonia 交给我们的那一个）
```

四条约定：

1. **场景图归渲染线程独占。** 界面线程只往邮箱里放一个 `float` 快照，绝不跨线程碰场景对象。
2. **失败一律降级，绝不白屏。** 缺模型 → 场景只剩网格与坐标轴；拿不到桌面 GL
   （无显卡 / 平台选了 ANGLE-ES / headless 截图）→ 控件不绘制，界面隐藏它，底层的主题化占位自然露出；
   原因经 `Failed` 事件同时写到界面文字与 stderr。
3. **两端各自换算。** 3D 只吃弧度（图形库内部单位），HUD 显示度数（给人看）——
   同一份 `/state` 数据的两种投影，互不牵就。
4. **图形栈的日志回到同一份日志。** 库内的日志门面（`RobotSimulation.Core.Utils.Logger`）在没人初始化时
   是个没有任何 provider 的空壳，所以 `App.CreateMainViewModel` 在建视口**之前**挂一次
   `SimulationLogBridge.Attach(new SimulationLogSink(log))`：URDF 资产解析 / 网格导入 / 模型装配
   就都出现在日志面板里，来源列是 `sim`。界面层看不到 M.E.L 的任何类型。

于是 `Scene3DView.axaml` 就是三层叠放：底层占位（无 GL 时的空状态）、中层 `RobotViewport`、顶层角标（GPU / FPS / 拾取结果）。
右下角的朝向 gizmo 由图形库自己画（`RobotSimulation` 0.2.0 起默认开启），界面**不再自绘**坐标轴。

## 流程编排与状态机

`IRobotService` 的每个方法都是**无状态**的：调一次，发一条指令。但界面上的「手动控制」「扫查流程」
是**有顺序**的，不能用一堆散落的 bool 拼出来 —— 5 个 bool 有 32 种组合，其中 27 种是非法状态。
因此 `Services/Robot/Workflows/` 单独描述流程。

| 文件 | 职责 | 依赖 |
|---|---|---|
| `ScanStateMachine.cs` | 只描述「状态怎么变」：阶段枚举 + 转移表 | **零依赖**，可脱离网络 / 界面单独测试 |

三条使用规则：

1. **所有状态变化都走 `TryFire()` 一个入口。** 非法转移返回 `false` 且状态不变，
   调用方据此放弃发送命令。例：扫查执行中点手动控制会被拒绝，必须先点「停止」。
2. **`CanFire()` 与 `TryFire()` 用同一套判定** —— 界面按钮灰不灰，和点了能不能执行永远一致。
3. **状态机不认识 `IRobotService`。** 发命令、订阅异步事件的活儿留给上层 Workflow
   （`ScanWorkflow` / `ManualWorkflow`，待接入）。

### 扫查状态图

```
Idle ──► PreScanning ──► Posing ──► Planning ──► Ready ──► Executing ──► Completed
  ▲        ① 预扫描      ② 位姿      ③ 规划               ④ 执行          │
  │                                                                        │
  └────────── Stop / Reset ◄──────── Faulted ◄──────── Fail ◄───────────────┘
```

- `Stop` / `Reset` 在任何阶段都合法 → 回 `Idle` 并清空流程进度
- `Fail`（后端 error 事件）在任何阶段都合法 → 进 `Faulted`
- `Faulted` **只能** `Reset` 回 `Idle`，不支持重试
- `Posing` 阶段必须**起点和终点都记录**才能开始规划（子条件门禁，转移表之外单独守一道）
- 命令回执与异步事件**谁先到都算数**，迟到的那个是无害的自转移

## 扩展指南

```
新功能 → 先问一句「它认识界面吗？」，再决定放哪个项目：

  · 新增指令 / 事件       → RUSTool.Core/Communication/ProtocolConstants.cs 补常量
  · /sensor 二进制帧      → RUSTool.Core/Communication/BridgeProtocol.cs 增加解码方法
  · 新增业务服务（标定）  → RUSTool.Core/Services/ 下（复用 BridgeClient，或经 IRobotService）
  · 新增业务流程（标定）  → RUSTool.Core/Services/Robot/Workflows/ 下建两个文件：
                            · XxxStateMachine.cs — 阶段枚举 + 转移表（纯逻辑，配单测）
                            · XxxWorkflow.cs     — 发命令 + 订阅事件 → 驱动状态机
  · 新增日志实现          → RUSTool.UI/Services/Logging/（Core 只保留契约）
  · 新增界面 / VM / 样式   → RUSTool.UI/ 对应目录；配色一律走 Theme/ 的语义类，不要写值转换器
  · 调整配色 / 控件样式   → RUSTool.UI/Theme/（改 Tokens/Semantic.axaml 即可换肤，控件样式不用动）
  · 新增数据库（SQLite）  → 另开 RUSTool.Data 项目（Core 依赖它、界面再依赖 Core）
  · 新增 3D 仿真窗        → 界面加一个 View，渲染侧改 RUSTool.Visualization
                            （Silk.NET / OpenGL 依赖不得泄漏进界面层与逻辑层；
                              契约见 RUSTool.Visualization/README.md —— 界面只认识
                              RobotViewport（数据）与 ISimulationLogSink（日志））
```

## 拆分进度

| 项目 | 状态 | 说明 |
|---|---|---|
| `RUSTool.Core` | ✅ 已拆出 | Communication + Services.Robot + ILogService 契约 |
| `RUSTool.UI/Theme` | ✅ 已并入 UI | 设计系统：令牌 + 控件样式，只含 XAML 资源，不产出 C# 类型；原独立 `RUSTool.Theme` 工程已删除 |
| `RUSTool.UI` | ✅ 已是唯一应用 | 完整应用：界面 + 设计系统（`Theme/`）+ 业务接线（引用 Core）；原 `RUSTool/` 项目已删除 |
| `RUSTool.Visualization` | ✅ 已拆出 | 图形栈隔离容器：`RobotViewport`（`OpenGlControlBase` 宿主：GL 生命周期 / 每帧 / 相机拾取）+ `RobotScene`（URDF 模型 + 关节驱动）+ `SimulationLogBridge`（库日志接进项目日志器）+ 随编译复制到输出目录的模型资产 |
| `tests/RUSTool.Core.Tests` | ✅ 已建 | 扫查状态机 54 个用例；无需网络 / GL，`dotnet test` 即可跑 |

> 原 `RUSTool/` 项目已删除。它的界面能力（语义类配色、主题化）由 `RUSTool.UI` 取代；
> 业务能力（composition root、连接 / 使能 / 驱动切换、`LogService`、点动「按住走松手停」、
> 日志自动滚动）已全部迁入 `RUSTool.UI`，协议文档与 `app.manifest` / 图标也一并迁移。
> `RUSTool.Core` 与测试从未引用该项目，因此删除对它们是零影响（编译器可证）。

> `RUSTool.Visualization` 是**新增**工程（不是迁移）：把 3D 内核 `RobotSimulation`
> （`Core` / `Robot` / `OpenGL`，0.2.0，nuget.org 与本机离线源都有）接进 Avalonia。
> `RUSTool.UI` 只引用它、不引用 Silk.NET；无 GL 时（离屏截图、无显卡机器）它自动降级为设计好的空状态，
> 因此 `preview.sh` 的产出与以前一样可用。
> 库日志经 `SimulationLogBridge` 汇进项目日志器（来源列 `sim`）；总趋势是**图形细节下移给库** ——
> 例如右下角的朝向坐标轴已从界面自绘改成库自带的 gizmo（0.2.0 新增，默认开启）。

> 原 `RUSTool.Theme` 独立工程与 `tools/`（`RUSTool.Theme.Gallery` 主题画廊、
> `RUSTool.UI.Showcase` 演示副本）也已删除：设计系统整体并入 `RUSTool.UI/Theme/`，
> 资源 URI 变为 `avares://RUSTool.UI/Theme/...`，`RUSTool.UI.csproj` 不再引用主题工程；
> 主题对照与界面截图统一由 `RUSTool.UI/preview.sh` 承担。
