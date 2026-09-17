# RUSTool.UI —— 应用主项目

RUSTool 的**唯一应用**：Avalonia + MVVM，界面皮肤全部来自项目内的设计系统
（`Theme/`，原 `RUSTool.Theme` 工程已并入），业务能力全部来自 `RUSTool.Core`。
`dotnet run` 起来的就是它。

> 定位：这是产品本身，不是界面草稿。原来的 `RUSTool/` 项目已删除 ——
> 它的界面能力被本项目取代，业务能力（composition root、连接逻辑、日志服务、
> 点动交互、日志自动滚动）已全部迁入本项目。

---

## 一、有什么 / 没有什么

| | 现状 |
|---|---|
| 界面 | ✅ 完整：共享工具栏（菜单 / 状态灯 / 驱动切换 / 急停）+ 工程师工作区 + 临床工作区 |
| 主题 | ✅ 全部走项目内设计系统 `Theme/`：按钮变体、语义文字类、状态灯、卡片布局类 |
| MVVM | ✅ ViewModel 只暴露状态与命令、**没有一个值转换器**（配色走主题语义类） |
| 业务 | ✅ 已接入 `RUSTool.Core`：`IRobotService` / `RobotSession` / `ILogService` |
| 通信 | ✅ 经 `BridgeClient` 连后端 bridge（WebSocket：`/control` `/state` `/sensor`） |
| 3D | ✅ 已接 `RUSTool.Visualization`：真实 GL 视口 + 图形库自带的朝向 gizmo；拿不到桌面 GL 时降级为主题化空状态 |
| 影像 / 曲线 | ⬜ 占位：静态图形与装饰性曲线，等真实数据源接入后替换 |

**界面上的读数、日志、状态全部来自真实后端。** 连上 bridge 才有数据；
没连上时工具栏显示「未连接 / 空闲」、HUD 读数为 0、日志为空 —— 这是正确行为，不是坏了。

截图（`./preview.sh`）不连后端，看到的就是这个离线态；要看真实数据请 `./preview.sh window`。

---

## 二、怎么跑

```bash
cd RUSTool.UI

./preview.sh                   # 真实窗口，工程师模式（可交互）
./preview.sh clinical          # 真实窗口，临床模式
./preview.sh light             # 截图：工程师模式 · 浅色
./preview.sh dark              # 截图：工程师模式 · 深色
./preview.sh clinical-light    # 截图：临床模式 · 浅色
./preview.sh clinical-dark     # 截图：临床模式 · 深色
./preview.sh all               # 四张一次拍全
./preview.sh popup MenuFile    # 展开"文件"菜单后截图（菜单是 Popup，不展开拍不到）
```

截图落在 `RUSTool.UI/preview/`（已在 `.gitignore` 里忽略）。
脚本会自己设好 `DOTNET_ROOT` / `PATH`（本机 dotnet 装在 `~/.dotnet`，没进 PATH）。

**目标框架 ≠ 构建 SDK。** 四个工程都是 `net8.0`（跟随 `RobotSimulation` 库），但构建要用
**.NET SDK 10**（本机 `~/.dotnet`）。用 SDK 8 跑 `dotnet build` 会失败：Avalonia 12 的源生成器
要求 Roslyn 4.14+，加载不上就没人生成 `InitializeComponent`，整片报 `CS0103`。所以本仓库**不放**
`global.json` 去钉 SDK 版本（钉了就编译不过）。

本项目已在 `RUSTool.sln` 里，也可以整解决方案一起构建：

```bash
dotnet build RUSTool.sln
```

---

## 三、目录结构

```
RUSTool.UI/
├── RUSTool.UI.csproj           WinExe · net8.0 · 引用 RUSTool.Core；内含设计系统 Theme/
├── Program.cs                  进程入口：正常启动 / --shot 离屏截图（复用同一份组装）
├── App.axaml(.cs)              应用入口 + 依赖图组装（composition root）
├── app.manifest                应用程序清单
├── preview.sh                  跑界面 / 拍截图
├── README.md                   本文件
│
├── Services/Logging/
│   ├── LogService.cs           ILogService 的 Avalonia 实现（Dispatcher marshal + 落盘）
│   └── SimulationLogSink.cs    3D 图形栈的日志出口实现（ISimulationLogSink → ILogService，来源 sim）
│
├── Theme/                      【设计系统】原独立工程 RUSTool.Theme 已并入
│   ├── Theme.axaml             唯一入口：汇总令牌 + 控件样式（App.axaml 只引用它）
│   ├── Tokens/                 Palette / Semantic / Metrics / Typography
│   ├── Bridges/Fluent.axaml    把 Fluent 资源键重定向到语义色（换肤的关键）
│   ├── Controls/               Base / Buttons / Menus / Overlays
│   └── README.md               设计系统的用法说明
│
├── Styles/
│   └── AppLayout.axaml         【应用级】布局类：card / cardHeader / panelTitle /
│                               sunken / canvas / toolbar / segLeft·segRight / tag
│
├── Assets/                     应用图标等静态资源
├── Docs/                       协议与设计文档（websocket_api / UI / avalonia_client_design）
│
├── ViewModels/                 8 个文件
│   ├── ViewModelBase.cs        ObservableObject 基类
│   ├── MainViewModel.cs        组装点：工具栏状态 + 模式切换
│   ├── SessionViewModel.cs     连接 / 使能 / 驱动 / 急停（连接类动作的唯一入口）
│   ├── RobotStatusViewModel.cs 订阅 /state 状态流 → HUD 读数（弧度→度在这一层换算）
│   ├── RobotControlViewModel.cs 点动 6 轴 + movej / movel / 暂停 / 复位
│   ├── ScanWorkflowViewModel.cs 四步扫查流程（同步回执 + 异步事件共同推进）
│   ├── ReplayViewModel.cs      回放：时间轴 / 播放 / 速度 / A-B 循环
│   └── LogViewModel.cs         日志面板：镜像 ILogService 的集合 + 级别过滤
│
└── Views/
    ├── MainWindow.axaml(.cs)   ← 与 ViewModels/MainViewModel 对应
    ├── Debug/                  工程师工作区
    │   ├── DebugWorkspace.axaml        上排 3D/影像/曲线 + 下排 指令/回放/日志
    │   ├── Scene3DView.axaml           3D 场景：占位层 / RobotViewport / 角标 三层叠放
    │   ├── RobotStatusOverlay.axaml    3D 右上角 HUD
    │   ├── UltrasoundView.axaml        超声影像区
    │   ├── ChartPanel.axaml            数据曲线 + 图例联动
    │   ├── ArmControlPanel.axaml       点动 + movej / movel（点动由 code-behind 驱动）
    │   ├── ScanWorkflowPanel.axaml     四步扫查流程
    │   ├── ReplayModule.axaml          回放模块
    │   └── LogView.axaml               日志（分列 + 语义色 + 自动滚到底）
    └── Clinical/
        └── ClinicalWorkspace.axaml     临床模式：单流水线 + 急停
```

---

## 四、MVVM 分层约定

**View —— 几乎没有代码。** 大多数 `.axaml.cs` 只有 `InitializeComponent()`，行为通过绑定表达。
只有两处例外，而且都是"控件自己的事"，放进 ViewModel 反而别扭：

| View | code-behind 干什么 | 为什么不做成命令 |
|---|---|---|
| `ArmControlPanel` | 指针按下 → `start_jog`，松开 → `stop_jog_decel` | 真机点动是「按住走、松手停」，Button 的 Click 表达不了 |
| `LogView` | 日志集合变化时把列表滚到底 | 滚动位置是控件状态，ViewModel 不该知道 |

视图之间是显式引用（`<debug:LogView DataContext="{Binding Log}" />`），没有反射式 ViewLocator：
组合关系是固定的，显式写出来更好读，也不会被裁剪掉。

**ViewModel —— 只报状态，不报颜色。** 状态到配色的映射不写值转换器：

```xml
<!-- ViewModel 只给互斥布尔量，颜色交给主题的语义类 -->
<Ellipse Classes="dot" Classes.success="{Binding IsConnected}" Classes.idle="{Binding IsIdle}" />
```

于是配色调整只改 `Theme/Tokens/Semantic.axaml` 一处，界面文件一个字都不用动。
四个状态灯位（空闲 / 手动 / 扫查中 / 已暂停）就是靠一组互斥布尔量叠在同一个 `Ellipse` 上实现的。

**ViewModel —— 只依赖接口。** 所有 VM 构造函数收的都是 `IRobotService` / `RobotSession` /
`ILogService`，没有一处 `new` 具体实现；唯一的例外是 `App.CreateMainViewModel`
（composition root）。换后端（真机 / 仿真 / 回放）只改那一个方法。

**设计系统与应用的分工。** `Theme/` 只回答「控件长什么样」；`Styles/AppLayout.axaml`
回答「卡片怎么摆、面板头多高」。前者换配色不动布局，后者改布局不动配色。

---

## 五、业务是怎么接进来的

依赖图在 `App.axaml.cs` 的 `CreateMainViewModel` 里组装，全项目只有这一处 `new` 具体实现：

```csharp
var bridge = new BridgeClient();
ILogService log = new LogService();                        // 契约在 Core，实现在界面层
bridge.Logger = (m, e) => log.Log(m, e ? LogLevel.Error : LogLevel.Info, "bridge");
SimulationLogBridge.Attach(new SimulationLogSink(log));     // 3D 图形栈的日志 → 同一份日志（来源 sim）
IRobotService robot = new RobotService(bridge);
var session = new RobotSession();                          // 全局共享状态（单例注入）
return new MainViewModel(robot, session, log);
```

数据流：

```
/state 状态流 ──► RobotStatusViewModel.OnStateUpdated ──► Post 到 UI 线程 ──► HUD 读数
                   （弧度 → 度在这一层换算；接触力由关节力矩模和近似）

界面动作 ──► SessionViewModel / RobotControlViewModel / ScanWorkflowViewModel
          ──► IRobotService ──► BridgeClient ──► 后端 bridge
          ◄── CommandResult（回执）：写日志 + 更新状态灯
          ◄── EventNotification（plan_done 等长任务事件）：推进扫查流程
```

几个刻意的设计：

- **连接类动作只有一个入口**（`SessionViewModel`）：其余 VM 只读 `RobotSession`、不发连接指令，
  避免同一状态多处拷贝后不同步。
- **扫查流程的完成标志有两个来源**：短指令（set_start_pose…）看回执；长任务（建图 / 规划 / 扫查）
  看后端异步事件（`pre_scan_done` / `plan_done` / `motion_done`）—— 前端不靠计时去猜。
- **日志只有一份**：`LogService` 维护 `ObservableCollection<LogEntry>`（限长 500 + 按天落盘），
  `LogViewModel` 对它做增量镜像并按级别过滤 —— "只看警告"因此是真的会过滤。
  3D 图形栈（`RobotSimulation`）的日志也汇进这一份：composition root 先挂
  `SimulationLogBridge.Attach(new SimulationLogSink(log))`，来源列显示 `sim`（`sim·AssetResolver` 之类
  按库内模块细分）。挂载必须早于任何库日志 —— 库的日志门面只认第一次初始化。
- **点动是「按住走、松手停」**：`ArmControlPanel` 的 code-behind 在指针按下/松开时分别下发
  `start_jog` / `stop_jog_decel`，并处理"指针被系统抢走"（`PointerCaptureLost`）以免机械臂一直走。
- **截图与正式启动共用同一份组装**，所以 `preview/*.png` 画的就是运行时那个界面。

---

## 六、和另外几个项目的关系

| 项目 | 关系 |
|---|---|
| `RUSTool.Core` | 唯一业务依赖：通信协议 / 传输 + 机器人服务 + 服务契约。不得引用 Avalonia |
| `RUSTool.Visualization` | 唯一允许出现 3D 图形栈（Silk.NET / OpenGL / `RobotSimulation`）的工程；本项目只引用它，且只认识两个契约：`RobotViewport`（数据）与 `ISimulationLogSink`（日志） |
| `Theme/`（本项目内） | 设计系统：只含 XAML 资源（令牌 + 控件样式），不产出 C# 类型。原 `RUSTool.Theme` 工程已并入 |
| `tests/RUSTool.Core.Tests` | 测 Core 的纯逻辑（扫查状态机）。不依赖网络 / 界面 / 图形栈 |

> 原 `tools/RUSTool.Theme.Gallery`（主题对照画廊）与 `tools/RUSTool.UI.Showcase`
> （本项目的早期演示副本）**已删除** —— 前者随设计系统并入本项目而失去必要，
> 后者早已标注冗余。

**命名注意**：本项目叫 `RUSTool.UI` 而不是 `RUSTool.App` ——
`App.axaml.cs` 里已经有 `partial class App`，命名空间与类型同名会导致 `CS0434`。

---

## 七、已知边界

- **影像 / 曲线仍是占位**：曲线是两条装饰性正弦（红 Fx、绿 Fy），不是真实力信号。
  3D 已接真实图形栈（`RUSTool.Visualization` → `RobotSimulation` 0.2.0）；拿不到桌面 GL 时
  控件隐藏、露出主题化空状态，这不是故障。
- **接触力是近似值**：状态帧里没有独立的接触力通道，HUD 的"末端接触力"用各关节力矩模和代替。
  后端一旦提供 `contact_force` 字段，只改 `RobotStatusViewModel.OnStateUpdated` 一行。
- **深色弹层圆角为 0 是有意为之**：见 `Theme/README.md` ——
  没有合成器时透明区会被渲染成黑色。确认有合成器后可改 `RadiusOverlay` / `ShadowOverlay`。
- **截图是离屏渲染**：菜单栏下拉属于 Popup，离屏会被托管到 OverlayLayer；
  X11 上它们是独立窗口 —— 要看弹层请用 `./preview.sh popup <菜单名>` 在真实窗口里拍。
- **日志会落盘**：`LogService` 默认写 `logs/`（相对进程工作目录），已在 `.gitignore` 里忽略。
