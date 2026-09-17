# RUSTool.UI 界面设计与实现（简体中文）

> 状态：设计已落地，实现随代码演进。配套：[`../README.md`](../README.md)（仓库入口）、[`../architecture/zh-CN.md`](../architecture/zh-CN.md)（工程边界与 ADR）、[`../core/zh-CN.md`](../core/zh-CN.md)（业务层）、[`../visualization/zh-CN.md`](../visualization/zh-CN.md)（3D 契约）、[`../testing/zh-CN.md`](../testing/zh-CN.md)（怎么跑起来）。

`RUSTool.UI` 是**产品本身**（不是界面草稿）：Avalonia + MVVM，`dotnet run` 起来的就是它。
它同时是唯一的 application 工程、唯一的组合根、以及设计系统的宿主。

---

## 1. 工程总览

### 1.1 有什么 / 没有什么

| | 现状 |
|---|---|
| 界面 | ✅ 完整：共享工具栏（菜单 / 状态灯 / 驱动切换 / 急停）+ 工程师工作区 + 临床工作区 |
| 主题 | ✅ 全部走项目内设计系统 `Theme/`：按钮变体、语义文字类、状态灯、卡片布局类 |
| MVVM | ✅ ViewModel 只暴露状态与命令、**没有一个值转换器**（配色走主题语义类） |
| 业务 | ✅ 已接入 `RUSTool.Core`：`IRobotService` / `RobotSession` / `ILogService` |
| 通信 | ✅ 经 `BridgeClient` 连后端 bridge（WebSocket：`/control` / `/state` / `/sensor`） |
| 3D | ✅ 已接 `RUSTool.Visualization`：真实 GL 视口 + 图形库自带的朝向 gizmo；拿不到桌面 GL 时降级为主题化空状态 |
| 影像 / 曲线 / 回放 | ⬜ 占位：静态图形与装饰性曲线，等真实数据源接入后替换 |

**界面上的读数、日志、状态全部来自真实后端。** 连上 bridge 才有数据；
没连上时工具栏显示「未连接 / 空闲」、HUD 读数为 0、日志为空 —— 这是正确行为，不是坏了。
截图（`./preview.sh`）不连后端，看到的就是这个离线态；要看真实数据请 `./preview.sh window`。

### 1.2 目录结构

```
RUSTool.UI/
├── RUSTool.UI.csproj           WinExe · net8.0 · 引用 RUSTool.Core + RUSTool.Visualization
├── Program.cs                  进程入口：正常启动 / --shot 离屏截图（复用同一份组装）
├── App.axaml(.cs)              应用入口 + 依赖图组装（composition root）
├── app.manifest                应用程序清单
├── preview.sh                  跑界面 / 拍截图
├── README.md                   工程级说明（导航到本文）
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
├── ViewModels/                 8 个文件
│   ├── ViewModelBase.cs        ObservableObject 基类
│   ├── MainViewModel.cs        组装点：工具栏状态 + 模式切换
│   ├── SessionViewModel.cs     连接 / 使能 / 驱动 / 急停（连接类动作的唯一入口）
│   ├── RobotStatusViewModel.cs 订阅 /state 状态流 → HUD 读数（弧度→度在这一层换算）
│   ├── RobotControlViewModel.cs 点动 6 轴 + movej / movel / 暂停 / 复位
│   ├── ScanWorkflowViewModel.cs 四步扫查流程（同步回执 + 异步事件双来源）
│   ├── LogViewModel.cs         日志面板（增量镜像 + 级别过滤）
│   └── ReplayViewModel.cs      记录 / 回放（占位）
│
└── Views/
    ├── MainWindow.axaml        窗口外壳：工具栏 + 两个工作区切换
    ├── Debug/                  工程师工作区
    │   ├── DebugWorkspace.axaml        上排 3D / 影像 / 曲线，下排 指令 / 回放 / 日志
    │   ├── Scene3DView.axaml           三层叠放：占位层 + RobotViewport + 角标
    │   ├── UltrasoundView.axaml        超声影像（占位）
    │   ├── ChartPanel.axaml            数据曲线（占位）
    │   ├── ArmControlPanel.axaml       点动 / MoveJ / MoveL（「按住走、松手停」）
    │   ├── ScanWorkflowPanel.axaml     四步流程面板
    │   ├── RobotStatusOverlay.axaml    状态 HUD
    │   ├── ReplayModule.axaml          回放模块
    │   └── LogView.axaml               日志面板
    └── Clinical/               临床工作区
        └── ClinicalWorkspace.axaml     扫查主视图 + 4 步向导 + 常驻急停
```

## 2. 两类用户与设计原则

### 2.1 目标

规范超声机械臂扫查系统的界面设计，明确区分两类使用者：**临床医生/技师** 与 **工程师**。二者共享同一后端与数据源，但界面呈现、可操作指令与安全约束完全不同。

### 2.2 两类用户与心智模型

| | 临床医生/技师 | 工程师 |
|---|---|---|
| 目标 | 为病人完成一次超声扫查 | 联调、诊断、调参、逐条验证协议 |
| 心智模型 | 一条**线性流水线** | 流水线 + **工具箱** |
| 关心的数据 | 就绪/警告/异常三色灯、扫查进度 | 关节角、力矩、原始回执、帧率、日志 |

### 2.3 设计原则

| 原则 | 说明 |
|------|------|
| **角色隔离** | 临床界面只暴露扫查流水线 + 急停；工程师界面暴露全部指令 |
| **数据同源** | 两种模式共享同一后端驱动与数据模型，行为一致 |
| **性能优先** | UI 渲染线程与物理仿真线程（1000Hz）严格分离 |
| **安全第一** | 临床界面屏蔽一切可能误触的调试入口；急停始终可及 |

---

## 3. 核心设计方法（三根支柱）

1. **按「心智模型」分界面，不按「指令类别」分**
   - 医生 = 一条线性扫查流水线（阶段向导）
   - 工程师 = 流水线 + 工具箱

2. **两套界面共享同一个状态机内核**，只是「投影」不同（向导 vs 工具箱），绝不复制业务逻辑。

3. **控件按「阶段」门控**
   - 点动只在「预扫查建图」阶段出现，扫查时消失
   - 急停永远常驻，任何阶段可达

---

## 4. 指令分层

后端指令（`ProtocolConstants.cs`）天然分两层，这是界面划分的依据。

### 4.1 工作流层（线性流水线，两套界面共用）

`connect` / `disconnect` / `is_connected` / `robot_enable` / `pre_scan_start` / `pre_scan_end` / `set_start_pose` / `set_end_pose` / `plan` / `execute` / `pause` / `resume` / `stop` / `reset` / `query_prescan_done` / `query_motion_done` / `is_motion_done`

### 4.2 工具箱层（工程师专属）

`movej` / `movel` / `servoj` / `servo_cart` / `servo_start` / `servo_end` / `start_jog` / `stop_jog_decel` / `stop_jog_immediate` / `switch_driver` / `get_state` / `run_file` / `set_time_speed` / `get_time_speed` / `get_sim_time` / `get_frame_rate` / `step_once` / 录制回放类

> 例外：`start_jog`（点动）两套界面都要——临床在「预扫查建图」阶段以**简化方向键**形式暴露，工程师以**完整 6 轴**形式暴露。二者走同一条 `start_jog` 指令。

### 4.3 指令 → 界面映射

| 指令 | 临床 | 工程师 |
|------|:---:|:---:|
| connect / disconnect / is_connected | ✓（状态自动） | ✓（显式操作） |
| robot_enable | ✓（扫查时自动） | ✓（显式） |
| pre_scan_start / end / query_prescan_done | ✓（阶段①） | ✓（阶段①） |
| set_start_pose / set_end_pose（点云选点） | ✓（阶段②） | ✓（点云选点或手输坐标） |
| plan / execute / pause / resume / stop / reset | ✓（阶段③④） | ✓（阶段③④） |
| query_motion_done / is_motion_done | ✓（进度自动） | ✓（手动查询） |
| start_jog（点动） | ✓ 简化方向键（仅阶段①） | ✓ 完整 6 轴 |
| movej / movel | — | ✓ |
| servoj / servo_cart / servo_start / end | — | ✓ |
| switch_driver / get_state / run_file | — | ✓ |
| 仿真（set_time_speed / step_once / …） | — | ✓ |
| 录制 / 回放 | — | ✓ |

---

## 5. 扫查状态机（共享内核）

两套界面驱动**同一条流水线**，业务逻辑不复制：内核是 `RUSTool.Core` 的
`ScanStateMachine`（8 个阶段 + 转移表，零依赖、54 个用例锁定），界面侧是
`ScanWorkflowViewModel` 的四步展示。

```
未连接 ──connect──▶ 已连接 ──robot_enable──▶ 已使能
已使能 ──pre_scan_start──▶ ① 建图中（点动扫体表，/sensor 流式生成点云）
① ──pre_scan_end──▶ (pre_scan_done) 点云建成
点云建成 ──[点云选起点]──▶ set_start_pose(p1)
起点已选 ──[点云选终点]──▶ set_end_pose(p2)
起点+终点已选 ──plan──▶ ③ 规划中 ──(plan_done)──▶ 规划完成
规划完成 ──execute──▶ ④ 扫查中 ──(scan_done)──▶ 扫查完成
任意态 ──stop──▶ 已停止；任意态 ──reset──▶ 已使能
```

**状态更新来源（双通道）**：

- 同步回执：`query_prescan_done` / `query_motion_done` 等查询指令的 `result`。
- 异步事件：`pre_scan_done` / `plan_done` / `scan_done` / `motion_done` / `error`（`EventReceived`）。

状态机对外输出「当前阶段 + 下一步操作」，供两套界面各自渲染。

**界面与内核的映射（当前实现）**

| 设计侧 | 内核（`RUSTool.Core`） | 界面（`RUSTool.UI`） |
|---|---|---|
| 8 阶段转移表 | `ScanStateMachine`（`Idle` → … → `Completed` / `Faulted`） | —（目前未接线） |
| 四步展示 | — | `ScanWorkflowViewModel.Steps`（`ScanStep`：`Pending` / `Active` / `Done`） |
| 门控 | `CanFire()` / `TryFire()` | 每步按钮的 `CanExecute`（按步骤状态 + 回执 / 事件推进） |
| 双通道状态更新 | `EndPreScan`（回执）与 `PreScanDone`（事件）互为无害自转移 | `OnEvent` 订阅 `pre_scan_done` / `plan_done` / `motion_done` / `scan_done`；短指令看回执 |

> **待接线**：把 `ScanWorkflowViewModel` 的步骤推进改为驱动 `ScanStateMachine`，
> 并把「扫查中禁止手动控制」交给 `RobotSession.TryEnter*` 仲裁 —— 这两条是同一件事的两半，
> 见 [`../core/zh-CN.md`](../core/zh-CN.md) 第 8 / 9 节的「接线现状」。

---

## 6. 临床应用模式（Operator Mode）

面向超声医生/技师，极简、触控友好、安全。

### 6.1 整体布局

```
┌──────────────────────────────────────────────────────────────┐
│ 状态栏: ●就绪 ●已连接 ●已使能     当前阶段: ② 点云选点    [🛑急停]│
├────────────────────────────────────┬─────────────────────────┤
│                                    │  ┌ 阶段步骤 ────────┐  │
│        主视图（随阶段切换）           │  │ ① 预扫查建图   ✓  │  │
│                                    │  │ ② 点云选点    ▶  │  │
│  ① 建图: 3D点云 + 探头实时位姿      │  │ ③ 规划         │  │
│  ② 选点: 3D点云(放大) + 选中点标记   │  │ ④ 执行扫查     │  │
│  ③ 规划: 点云 + 规划路径预览线       │  └────────────────┘  │
│  ④ 扫查: 超声影像(主) + 轨迹(辅)     │  ┌ 阶段控件 ────────┐  │
│                                    │  │  ① 时: 点动键盘    │  │
│                                    │  │  ② 时: 选点提示    │  │
│                                    │  │  ③ 时: [开始规划]  │  │
│                                    │  │  ④ 时: [⏸][⏹]    │  │
│                                    │  └────────────────┘  │
├────────────────────────────────────┴─────────────────────────┤
│ 患者:张三 ID:20260813 部位:腹部 │ 提示: 请在点云上点击选择起点  │
└──────────────────────────────────────────────────────────────┘
```

### 6.2 阶段向导

右侧永远显示「你在第几步、下一步做什么」，主视图和控件跟随阶段切换：

| 阶段 | 主视图 | 阶段控件 |
|------|--------|---------|
| ① 预扫查建图 | 3D 点云（实时生成）+ 探头位姿 | 方向键点动 + [结束预扫查] |
| ② 点云选点 | 3D 点云（放大）+ 选中点标记 | 鼠标/触控选点 |
| ③ 规划 | 点云 + 规划路径预览线 | [开始规划] |
| ④ 执行扫查 | 超声影像（主）+ 3D 轨迹（辅） | [暂停] [停止] |

### 6.3 临床点动键盘

复用 `start_jog`（ref=基坐标，固定低速，max_dis=0 无限），UI 呈现为 6 个方向键；按压=点动，松手=停。

```
              ┌─────┐
              │ 上  │      ← Z+
      ┌─────┐ ├─────┤ ┌─────┐
      │ 左  │ │ 后  │ │ 右  │   ← Y- / X- / Y+
      └─────┘ └─────┘ └─────┘
              │ 前  │      ← X+
              ├─────┤
              │ 下  │      ← Z-
```

隐藏参考系切换与速度调节，只做「把探头挪到病人体表上」这一件事。

### 6.4 交互与安全规则

- **开始/暂停/停止** 是唯一高频操作，放大 + 配色 + **长按确认**（防误触）。
- **急停永远可见**：软件急停常驻 + 物理急停兜底。
- **状态只显示三色灯**（就绪/警告/异常），不显示原始数值。
- **进度驱动**：用 `query_motion_done` + 事件推算百分比。
- **键盘**：屏蔽除急停外的按键（`PreviewKeyDown`）。
- **右键菜单**：全局禁用 `ContextMenu`。
- **全屏**：`WindowState="FullScreen"`，`WindowDecorations="False"`。

---

## 7. 工程师模式（Engineer Mode）

面向开发者，全量、可诊断、可逐条验证协议。

### 7.1 整体布局

```
┌──────────────────────────────────────────────────────────────────┐
│ 工具栏: [连接][断开] [上使能] [驱动:真实▼] │ 模式切换 │ [🛑急停] │ 状态点 │
├──────────────┬──────────────────────────┬─────────────────────────┤
│ 3D场景+HUD   │ 超声影像                 │ 数据曲线                │
├──────────────┴──────────────────────────┴─────────────────────────┤
│ ┌ 控制面板(TabControl) ──────────────┐  ┌ 回放 ─────────┐ ┌ 日志 ┐ │
│ │ [点动] [扫查流程] [伺服] [仿真] [驱动] │  │              │ │      │ │
│ │   当前 tab 内容（见下表）             │  │              │ │      │ │
│ └────────────────────────────────────┘  └──────────────┘ └──────┘ │
└──────────────────────────────────────────────────────────────────┘
```

### 7.2 控制面板标签页

| Tab | 内容 |
|-----|------|
| 点动 | 6 轴 jog + 参考系切换 + 速度/加速度 + MoveJ/MoveL 输入 + 停止 |
| 扫查流程 | 同一个状态机（紧凑行 + 状态灯）+ query 按钮 + 原始回执日志 |
| 伺服 | servo_start/end、servoj、servo_cart |
| 仿真 | 倍速、单步、仿真时间/帧率 |
| 驱动 | robot_enable、switch_driver、is_connected、get_state、run_file |

> **点动独占一个 tab 并占满面板宽度**：点动是最高频持续操作，需要最大空间，内部用 2 列 × 3 轴紧凑布局，避免横向溢出。

### 7.3 3D 场景图层

| 图层 | 说明 |
|------|------|
| 机械臂 DH 模型 | 真实尺寸连杆与关节 |
| 点云 | 预扫查生成的病人体表点云（验证建图质量） |
| TCP 坐标系 | 工具中心点 XYZ 三轴 |
| 规划路径线 | 预设扫查路径 |
| 实时轨迹 | TCP 实际运动轨迹（透明度渐变） |
| 力矢量箭头 | 末端接触力大小与方向 |
| 超声探头模型 | 探头姿态与扫查面朝向 |

---

## 8. 点动交互差异（临床 vs 工程师）

| | 临床（建图阶段） | 工程师 |
|---|---|---|
| 形式 | 简化方向键（前/后/左/右/上/下） | 完整 6 轴 + 参考系切换 |
| 速度 | 固定低速，不可调 | 0~100 可调 |
| 参考系 | 隐藏（锁定基坐标） | 关节/基/工具可切换 |
| 出现时机 | 只在①建图阶段 | 始终可及 |

---

## 9. 共用组件与设计规范

### 9.1 状态 HUD

半透明悬浮于 3D 场景右上角（`RobotStatusOverlay`），`IsHitTestVisible=False`。临床模式仅显示三色灯；工程师模式显示 TCP 位姿 / 关节角 / 力矩数值。

### 9.2 记录与回放模块

- 记录：超声影像、机械臂 6D 位姿、力/扭矩 6 维、控制指令与状态。
- 回放：时间轴拖拽、播放/暂停/步进、速度 0.1x~2.0x、A/B 循环、双轨联动（超声 + 曲线同步）。

### 9.3 主题与配色

| 场景 | 主色 | 强调色 |
|------|------|--------|
| 临床模式 | 白色/浅灰 (#FFFFFF/#F5F5F5) | 医疗蓝 (#1A73E8)，绿=正常/红=警告 |
| 工程师模式 | 深灰 (#1E1E1E) | 蓝色 (#007ACC) |

> 上表是**设计意图**；实际色值全部落在 `Theme/Tokens/Palette.axaml`（原始色阶）与
> `Theme/Tokens/Semantic.axaml`（Light / Dark 语义色）里，界面文件一律不写 hex ——
> 换句话说，这里的表格描述的是观感，不是可以直接改的地方（见第 10 节）。

### 9.4 数据绑定与性能

- `CommunityToolkit.Mvvm` 的 `ObservableProperty` / `RelayCommand`，启用编译绑定。
- 高频更新用 `WriteableBitmap` 复用实例避免 GC。
- 日志等长列表启用虚拟化。

---

## 10. 主题与设计系统

配色与控件观感不写在界面文件里，全部走 `Theme/`（只含 XAML 资源，不产出 C# 类型）：

```xml
<!-- App.axaml：顺序不能反 —— FluentTheme 提供模板，本主题负责重新上色 -->
<Application.Styles>
    <FluentTheme />
    <StyleInclude Source="avares://RUSTool.UI/Theme/Theme.axaml" />
</Application.Styles>
```

| 层 | 文件 | 职责 |
|---|---|---|
| ① 原始色阶 | `Theme/Tokens/Palette.axaml` | 唯一允许出现 `#RRGGBB` 的地方 |
| ② 语义色 | `Theme/Tokens/Semantic.axaml` | Light / Dark 各一套，控件只引用它（**换肤只改这里**） |
| ③ Fluent 桥接 | `Theme/Bridges/Fluent.axaml` | 把 Fluent 的资源键重定向到语义色 |
| ④ 控件样式 | `Theme/Controls/*.axaml` | 补 Fluent 没有的能力（按钮变体、文字类、菜单、浮层） |

应用级布局类另放在 `Styles/AppLayout.axaml`（`card` / `cardHeader` / `sunken` / `toolbar` / `tag` …）：
**`Theme/` 回答「控件长什么样」，`AppLayout` 回答「卡片怎么摆」** —— 前者换配色不动布局，后者改布局不动配色。

状态 → 颜色**不用值转换器**，而是让 VM 暴露互斥布尔量、界面叠加主题语义类：

```xml
<!-- ViewModel 只给互斥布尔量，颜色交给主题的语义类 -->
<Ellipse Classes="dot" Classes.success="{Binding IsConnected}" Classes.idle="{Binding IsIdle}" />
```

四个状态灯位（空闲 / 手动 / 扫查中 / 已暂停）就是靠一组互斥布尔量叠在同一个 `Ellipse` 上实现的。
用法细节（按钮变体、文字类、状态灯类、弹层圆角为什么默认是 0）见
[`../../RUSTool.UI/Theme/README.md`](../../RUSTool.UI/Theme/README.md)。

---

## 11. 业务接线：组装根与数据流

### 11.1 组装根（composition root）

依赖图在 `App.axaml.cs` 的 `CreateMainViewModel` 里组装，**全项目只有这一处 `new` 具体实现**：

```csharp
var bridge = new BridgeClient();
ILogService log = new LogService();                        // 契约在 Core，实现在界面层
bridge.Logger = (m, e) => log.Log(m, e ? LogLevel.Error : LogLevel.Info, "bridge");
SimulationLogBridge.Attach(new SimulationLogSink(log));     // 3D 图形栈日志 → 同一份日志（来源 sim）
IRobotService robot = new RobotService(bridge);
var session = new RobotSession();                          // 全局共享状态（单例注入）
return new MainViewModel(robot, session, log);
```

`MainViewModel` 再按固定顺序建 6 个子 VM（日志最先，其余 VM 都要往里写）：

```
MainViewModel
├── Session     SessionViewModel         连接 / 使能 / 驱动 / 急停
├── Status      RobotStatusViewModel     /state → HUD 读数
├── Control     RobotControlViewModel    点动 + MoveJ / MoveL
├── Scan        ScanWorkflowViewModel    四步扫查流程
├── Log         LogViewModel             日志面板
└── Replay      ReplayViewModel          记录 / 回放（占位）
```

**所有 VM 构造函数收的都是接口**（`IRobotService` / `RobotSession` / `ILogService`），
没有一处 `new` 具体实现；换后端（真机 / 仿真 / 回放）只改那一个方法。

### 11.2 数据流

```
/state 状态流 ──► RobotStatusViewModel.OnStateUpdated ──► Post 到 UI 线程 ──► HUD 读数
                   （弧度 → 度在这一层换算；接触力由关节力矩模和近似）
                        │ XAML 绑定（编译期检查）
                        ▼
                   RobotViewport.JointValues ──► RobotScene.ApplyJointValues（渲染线程）

界面动作 ──► SessionViewModel / RobotControlViewModel / ScanWorkflowViewModel
          ──► IRobotService ──► BridgeClient ──► 后端 bridge
          ◄── CommandResult（回执）：写日志 + 更新状态灯
          ◄── EventNotification（plan_done 等长任务事件）：推进扫查流程
```

### 11.3 分层映射

- **共享**：`ScanWorkflowViewModel`（流水线）+ `IRobotService` + `RobotStatusViewModel`（HUD）+ `RobotSession`。
- **工程师独有**：手动 / 伺服 / 仿真 / 驱动相关面板。
- **临床独有**：患者信息、进度向导、长按确认等外壳。

几个刻意的设计：

- **连接类动作只有一个入口**（`SessionViewModel`）：其余 VM 只读 `RobotSession`、不发连接指令。
- **扫查流程的完成标志有两个来源**：短指令（`set_start_pose` …）看回执；长任务（建图 / 规划 / 扫查）
  看后端异步事件（`pre_scan_done` / `plan_done` / `motion_done`）—— 前端不靠计时去猜。
- **日志只有一份**：`LogService` 维护 `ObservableCollection<LogEntry>`（限长 500 + 按天落盘），
  `LogViewModel` 对它做增量镜像并按级别过滤 —— 「只看警告」因此是真的会过滤；3D 图形栈日志同源。
- **点动是「按住走、松手停」**：`ArmControlPanel` 在指针按下 / 松开时分别下发
  `start_jog` / `stop_jog_decel`，并处理「指针被系统抢走」（`PointerCaptureLost`）以免机械臂一直走。
- **截图与正式启动共用同一份组装**，所以 `preview/*.png` 画的就是运行时那个界面。

---

## 12. 运行与截图

```bash
cd RUSTool.UI

./preview.sh                   # 真实窗口，工程师模式（可交互）
./preview.sh clinical          # 真实窗口，临床模式
./preview.sh window --clinical # 真实窗口 + 透传任意启动参数
./preview.sh light             # 截图：工程师模式 · 浅色 -> preview/01-engineer-light.png
./preview.sh dark              # 截图：工程师模式 · 深色 -> preview/02-engineer-dark.png
./preview.sh clinical-light    # 截图：临床模式 · 浅色   -> preview/03-clinical-light.png
./preview.sh clinical-dark     # 截图：临床模式 · 深色   -> preview/04-clinical-dark.png
./preview.sh all               # 四张一次拍全
./preview.sh popup MenuFile    # 展开「文件」菜单后截图（菜单是 Popup，不展开拍不到）
```

脚本自己设好 `DOTNET_ROOT` / `PATH`（本机 dotnet 装在 `~/.dotnet`，没进 PATH），
并把工作目录固定到仓库根 —— 截图路径因此是确定的。产物落在 `RUSTool.UI/preview/`（已在 `.gitignore` 里忽略）。

**目标框架 ≠ 构建 SDK。** 四个工程都是 `net8.0`（跟随 `RobotSimulation` 库），但构建要用
**.NET SDK 10**：Avalonia 12 的源生成器要求 Roslyn 4.14+，用 SDK 8 会加载不上、整片报 `CS0103`。
所以本仓库**不放** `global.json` 去钉 SDK 版本（钉了就编译不过）。细节见 [`../testing/zh-CN.md`](../testing/zh-CN.md)。

---

## 13. 已知边界与待办

### 13.1 已知边界

| 现象 / 边界 | 说明 |
|---|---|
| 界面读数为 0、日志为空 | 没连上 bridge 的**离线态**，属正确行为；`./preview.sh window` 起来后点「连接」即可 |
| 3D 区是空的（主题化空状态） | 拿不到桌面 OpenGL（离屏截图 / 无显卡机器 / GLES 上下文）时控件自己隐藏并走 `Failed`；后端只带 `#version 330 core` 着色器，遇到 GLES 会抛 `NotSupportedException` 并被捕获 |
| 截图里看不到下拉菜单 | 菜单栏下拉是 Popup，离屏会被托管到 OverlayLayer；用 `./preview.sh popup <菜单名>` 在真实窗口里拍 |
| 「末端接触力」是近似值 | 状态帧里没有独立的接触力通道，目前用各关节力矩模和代替；后端一旦提供 `contact_force` 字段，只改 `RobotStatusViewModel.OnStateUpdated` 一处 |
| 影像 / 曲线 / 回放是占位 | 曲线是两条装饰性正弦（红 Fx、绿 Fy），不是真实力信号；回放时间轴也是演示数据 |
| 「急停」没有独立的后端通道 | 后端只回执 `stop`，所以急停是否按下是本地界面状态（`SessionViewModel.IsEmergencyStopped`） |
| 深色弹层圆角为 0 | 有意为之：没有合成器时透明区会被渲染成黑色；确认有合成器后可改 `Theme` 的 `RadiusOverlay` / `ShadowOverlay` |
| 日志会落盘 | `LogService` 默认写 `logs/`（相对**进程工作目录**），已在 `.gitignore` 里忽略 |

### 13.2 待办（设计与实现之间的缺口）

1. **状态机接线**：`ScanWorkflowViewModel` 的步骤门控改为驱动 `ScanStateMachine`；
   并把 `RobotSession.TryEnter*` 接进手动 / 扫查模式仲裁（当前只有 `ExitToIdle()` 被调用）。
2. **`/sensor` 点云通道**：`SensorTypes.PointCloud` 目前只是常量；需要二进制帧解码
   （`ConnectionManager` 已按整帧交付原始字节）与点云渲染。
3. **3D 点云渲染 + 选点**：`Scene3DView` 目前是「URDF 模型 + 关节驱动 + 拾取」，
   点云图层与「点击表面选点」（raycast）尚未接（落点是 `RobotScene.Graph`）。
4. **`set_start_pose` / `set_end_pose` 参数**：仍按「无参采集当前位姿」下发；
   若后端要求传点坐标 `[x,y,z]`（或点索引），需与后端确认后修正。
5. **回放模块**：时间轴、A/B 循环、双轨联动都还是演示数据。

---

## 附录：技术选型参考

| 组件 | 本项目采用 | 备注 |
|------|------|------|
| UI 框架 | **Avalonia 12.1.0** | 跨平台桌面；`OpenGlControlBase` 可直接嵌 GL |
| 架构模式 | **MVVM + CommunityToolkit.Mvvm 8.4.2** | 源生成器（`ObservableProperty` / `RelayCommand`） |
| 3D 渲染 | **`RobotSimulation` 0.2.0**（Silk.NET.OpenGL 2.23.0） | 自研图形库；隔离在 `RUSTool.Visualization` |
| 依赖注入 | 无容器，显式组合根（`App.CreateMainViewModel`） | 依赖图小而固定，引入容器反而多一层间接 |
| 日志 | **自研 `ILogService`**（契约在 Core、实现在界面层，按天落盘） | 与图形栈日志合流（来源列 `sim`） |
| 图表 | 占位（装饰性绘制） | 真实数据源接入时再选型 |

*本文档随实现同步更新。*
