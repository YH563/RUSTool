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
│   │   └── LogService.cs     — ILogService 的 Avalonia 实现（Dispatcher marshal + 落盘）
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
| **Services.Logging（实现）** | `RUSTool.UI` | Avalonia 实现：集合更新 marshal 到 UI 线程 + 落文件 | `LogService` |
| **ViewModels** | `RUSTool.UI` | UI 状态与命令；只依赖 Core 的接口 | `MainViewModel` 及各功能 VM |
| **Views** | `RUSTool.UI` | 界面呈现（配色走主题语义类，无值转换器） | AXAML 文件 |
| **Styles** | `RUSTool.UI` | 应用级布局类 | `AppLayout.axaml` |
| **Composition Root** | `RUSTool.UI` | 依赖图组装：全项目唯一 new 具体实现的地方 | `App.CreateMainViewModel` |
| **Visualization** | ⬜ 待拆为独立项目 | 3D 场景、数据图表 | 机器人仿真场景、实时曲线 |
| **Data** | ⬜ 待拆为独立项目 | 数据库/持久化 | SQLite、PostgreSQL 仓储实现 |
| **Infrastructure** | ⬜ 待拆为独立项目 | 跨切面基础设施 | 配置、IoC 容器、异常处理 |

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
  · 新增 3D 仿真窗        → 另开 RUSTool.Visualization 项目后由 RUSTool.UI 引用
                            （Silk.NET / OpenGL 依赖不得泄漏进界面层与逻辑层）
```

## 拆分进度

| 项目 | 状态 | 说明 |
|---|---|---|
| `RUSTool.Core` | ✅ 已拆出 | Communication + Services.Robot + ILogService 契约 |
| `RUSTool.UI/Theme` | ✅ 已并入 UI | 设计系统：令牌 + 控件样式，只含 XAML 资源，不产出 C# 类型；原独立 `RUSTool.Theme` 工程已删除 |
| `RUSTool.UI` | ✅ 已是唯一应用 | 完整应用：界面 + 设计系统（`Theme/`）+ 业务接线（引用 Core）；原 `RUSTool/` 项目已删除 |
| `RUSTool.Visualization` | ⬜ 待拆 | 嵌入 RobotSimulation 3D 渲染时拆出（图形栈的隔离容器） |
| `tests/RUSTool.Core.Tests` | ✅ 已建 | 扫查状态机 54 个用例；无需网络 / GL，`dotnet test` 即可跑 |

> 原 `RUSTool/` 项目已删除。它的界面能力（语义类配色、主题化）由 `RUSTool.UI` 取代；
> 业务能力（composition root、连接 / 使能 / 驱动切换、`LogService`、点动「按住走松手停」、
> 日志自动滚动）已全部迁入 `RUSTool.UI`，协议文档与 `app.manifest` / 图标也一并迁移。
> `RUSTool.Core` 与测试从未引用该项目，因此删除对它们是零影响（编译器可证）。

> 原 `RUSTool.Theme` 独立工程与 `tools/`（`RUSTool.Theme.Gallery` 主题画廊、
> `RUSTool.UI.Showcase` 演示副本）也已删除：设计系统整体并入 `RUSTool.UI/Theme/`，
> 资源 URI 变为 `avares://RUSTool.UI/Theme/...`，`RUSTool.UI.csproj` 不再引用主题工程；
> 主题对照与界面截图统一由 `RUSTool.UI/preview.sh` 承担。
