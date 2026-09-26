# RUSTool.Charts（简体中文）

> 状态：反映当前实现。配套：[`../README.md`](../README.md)（仓库入口）、[`../architecture/zh-CN.md`](../architecture/zh-CN.md)、[`../ui/zh-CN.md`](../ui/zh-CN.md)、[`../visualization/zh-CN.md`](../visualization/zh-CN.md)、[`../testing/zh-CN.md`](../testing/zh-CN.md)。

这个工程只有一个职责：**让 2D 图表栈（LiveCharts 2 + SkiaSharp）只在这里出现**。
`RUSTool.UI` 引用它，但只认识三个契约：`Controls/RobotStatePanel.axaml`（控件）、
`Data/RobotStateRow.cs`（一行的绑定源）与 `Data/RobotStateRows.cs`（建行 / 推帧 / 清空的静态入口）。

---

## 1. 它解决什么问题

```
RUSTool.UI  ──绑定──►  RobotStatePanel.Rows         （ObservableCollection<RobotStateRow>）
            ──绑定──►  RobotStatePanel.IsConnected  （bool，只决定空状态那句话的措辞）
            ──调用──►  RobotStatePanel.PushFrame(effort)   （一帧 = 一个 IReadOnlyList<double>）
                              │
                              ▼        ← 到此为止，界面的世界结束
                       RobotStateRow（每行：一路数据的滚动历史 = 点集合）
                              │
                              ▼
                       StateRowChart（CartesianChart）→ LiveChartsCore / SkiaSharp
```

| 关注点 | 归属 |
|---|---|
| 曲线的绘制、坐标轴、线宽、配色、主题跟随 | `StateRowChart` / `StatePalette`（本工程） |
| 滚动窗口（加尾 + 超限删头）、行头读数 | `RobotStateRow`（本工程，纯数据） |
| 通道目录（名字 + 单位） | `RobotArmChannels`（本工程） |
| 从状态帧里取哪一段数组、在哪个线程推 | `RUSTool.UI`（`TorqueChartViewModel`） |
| 卡片外壳（标题 / 窗口长度 / 圆角 / 位置） | `RUSTool.UI/Views/Debug/DebugWorkspace.axaml`（卡片头；样式来自宿主主题库） |

**一行一条曲线、一路一行**，而不是一张图六条线：卡片体只有几百像素高，六条线叠进去必然互相穿插；
拆成数行后每行一条，形状互不遮挡，行头还有「色标 + 通道名 + 最新读数」可对照 ——
色标取的就是这条线的颜色（同一个 `StatePalette`），所以「哪一行是哪一路」不用先读数字就能对上。
行高**不写死**：六行装在等分格子里（`UniformGrid`），面板多高就平分多高，行间留一道底色缝 ——
卡片体变大时曲线跟着长高，不会在下面留一片白（宿主需要给确定高度，见 `RobotStatePanel` 类注释）。

## 2. 数据契约（界面层需要知道的全部）

| 入口 | 类型 | 说明 |
|---|---|---|
| `Rows` | `ObservableCollection<RobotStateRow>?`（Avalonia 属性，可绑定） | 要画的行。**行由外部拥有**：控件不造行、也不换行；`null` / 空集 = 什么都还没得画 |
| `IsConnected` | `bool`（Avalonia 属性，可绑定） | 控制通道是否在线。只影响空数据态那句提示的措辞 |
| `HintText` | `string?`（Avalonia 属性，可绑定） | 覆盖空数据提示；`null`（默认）= 按 `IsConnected` 自动选一句 |
| `HasData` | `bool`（**只读**派生属性） | 任意一行有一个点。由点集合的变更自动翻；外部设不了（也设不进去） |
| `PushFrame(IReadOnlyList<double>)` | 方法，**必须在 UI 线程调用** | 推一帧：交给每一行，各行取自己那一路的分量；分量不足的那些按 0 计 |
| `ClearHistory()` | 方法，**必须在 UI 线程调用** | 清空所有行的历史与读数（重连后端时用：旧的形状不该冒充新数据） |
| `RobotStateRows.Create(channels, windowFrames)` | 静态工厂 | 按目录建一组行（第 i 行画第 i 路）；`windowFrames` 默认 `RobotStateRow.DefaultWindowFrames` = 300（按标称 20 Hz 约 15 s） |
| `RobotStateRows.PushFrame(rows, frame)` / `Clear(rows)` | 静态方法 | 面板与宿主 VM 共用的同一份「推 / 清」语义（宿主不持有面板实例时走这里） |
| `RobotArmChannels.Torques()` / `JointAngles()` / `FlangePose()` | 静态目录 | 目录顺序 = 数据数组的分量顺序（下标对齐） |
| `StateChannel(Name, Unit)` | `record` | 一路可画量：名字（行头标签）+ 单位（读数后缀）；`Format(v)` = 两位小数 + 单位 |

行模型（`RobotStateRow`）的公开面：`ChannelIndex`（画第几路，同时决定线色与行头色标）、
`Channel`（名字 + 单位）、`WindowFrames`、`Values`（点集合，绑定给图表 —— 它同时也是这一行的历史）、
`Current` / `CurrentText`（行头读数）、`Push(frame)` / `Clear()`。

单位约定：**本层不做任何换算** —— 目录里写什么单位，交进来的数组就必须已经是那个单位。
`Torques()` 是 `N·m`；`JointAngles()` 标的是**度**（协议里是弧度，换算由界面层做）；
`FlangePose()` 逐路写单位（`m` / `°`）。本层也**不认识** `BridgeProtocol.StateFrame`，
只认识 `IReadOnlyList<double>`：协议模型变了，图表库不该跟着变。

## 3. 线程契约与数据流

```
WS 线程  /state 帧 ──► TorqueChartViewModel.OnStateUpdated
                          │ Dispatcher.UIThread.Post
                          ▼
                       PushFrame(state.Effort) ──► RobotStateRow.Push（逐行）
                          │                        ├─ 取这一路的分量（分量不足按 0）
                          │                        ├─ Values 加尾 / 超窗口删头（集合实例始终不变）
                          │                        └─ Current / CurrentText 通知
                          ▼
                       StateRowChart / LiveCharts（自己的节流器把一帧内的多次变更合批再画）
```

- **改的是绑定源集合，所以只能在 UI 线程动。** 本层不替调用方 marshal：推帧那一层
  （`/state` 的订阅者）本来就知道自己在哪个线程上。
- **历史就是点集合本身**：一行只看一路，所以不再按通道分开存 —— 每来一帧取自己那一路的分量入队，
  超出窗口就删掉头上那个。长满一屏之后曲线开始往左滚，而不是每次重画都缩放成「刚好填满」。
- **点集合实例从不整体替换**：图表订阅的是集合实例的变更通知，换实例等于让它重挂监听
  （还丢掉这一帧的通知）。所以更新一律「删头 + 加尾」，`Clear()` 也是**就地**清空。

## 4. 空数据态

没有帧时 LiveCharts 画的是一圈空坐标轴 —— 看起来像「图坏了」。所以：

- 没数据就把整个曲线区撤掉（`ChartArea.IsVisible = false`），只留一句「为什么没有」。
- 措辞分两种：`IsConnected = false` → 「未连接控制通道 · 曲线待数据」（该去查连接）；
  连上了但没帧 → 「等待状态帧…」（该去查后端 / 状态流有没有开）。要更具体的说法就设 `HintText`。
- `HasData` 是**算出来的**：控件监听每行点集合的变更，有任何一个点就显示曲线区。
  调用方因此永远不用记得「推完帧还要把 `HasData` 置真」—— 忘一次就是「曲线在跳、提示语还挂在中间」。

## 5. 配色与主题

- 曲线颜色是**数据标识**（这条线是哪一路），不是控件状态：同一路数据在浅色 / 深色下必须是同一个
  颜色，所以它们**不进**主题的 `Tokens/Semantic.axaml`。语义色恰好相反 —— 同一根曲线换主题就该换色，
  那就认不出是哪一路了。唯一来源是 `StatePalette`（蓝 → 青 → 绿 → 黄 → 橙 → 紫，明度接近、
  色相均匀铺开，六行并排时彼此可分辨）；超出配色表按取模绕回来，而不是给一片灰。
- 主题跟随的是**布局与文字**这一侧：面板 XAML 取宿主的令牌
  （`SurfaceSunkenBrush` / `SurfaceBaseBrush` / `BorderSubtleBrush` / `RadiusSm` /
  `TextPrimaryBrush` / `TextSecondaryBrush` / `TextTertiaryBrush` /
  `FontMono` / `FontSizeBody` / `FontSizeCaption`），**令牌缺失就取默认值、不抛异常** ——
  本控件把宿主当「可选的上游」，换到别的宿主里也不该是一块没底色的方块。
  曲线区用「下沉底色 + 更亮的条带」分成六个窗口，两条都是令牌：换肤时缝与窗口一起跟着走。
- 行头那条色标走 <code>StatePalette.BrushFor</code>（同一个色表的 `IBrush` 表示，见
  <code>ChannelBrushConverter</code>）：色标与线**同表同色**，深色主题下也不变色。
- 坐标轴画在 Skia 画布上，`DynamicResource` 到不了那里：`StateRowChart` 订阅
  `ActualThemeVariantChanged`，换主题时用 `TryGetResource` 重取一遍（取不到就退回兜底色）。
  轴上画什么也在同一处定：**X / Y 刻度字都不画**（一行只有 ~50px 高，刻度字挤进去就是一坨糊字、
  还把绘图区压窄），只留一条零线当参考 —— 精确值在行头读数里。
- 面板样式表里抄了宿主几条规则（`Border.canvas` / `TextBlock.mono` / `caption`）的**规则本身**，
  类名换成自己的（`stateCanvas` / `stateRow` / `stateName` / `stateValue` / `stateHint`）：
  本控件是可复用的库成员，不该要求调用方「先给我导一套样式表」。
  抄的只是规则 —— 颜色与字号仍然取自主题令牌，换肤照样跟着走。

## 6. 依赖与版本约束

| 包 | 版本 | 作用 |
|---|---|---|
| `LiveChartsCore.SkiaSharpView.Avalonia` | **2.1.0-dev-798**（钉死） | 2D 图表（Avalonia 后端）。2.0.4 / 2.0.5 在 Avalonia 12.1.0 下会抛 `MissingFieldException`（`Avalonia.Input.Gestures.PinchEvent`）—— 它们链的是 Avalonia 11 的强签名字段，运行时找不到。升级前先读 `RUSTool.Charts.csproj` 里那段注释 |
| `Avalonia` | 12.1.0 | 控件本身是 Avalonia 控件：`UserControl` + `StyledProperty` + 主题资源查询 |
| `CommunityToolkit.Mvvm` | 8.4.2 | 行模型是绑定源（`ObservableObject` + `ObservableProperty`） |
| `SkiaSharp` | **故意不显式声明** | 版本必须跟着 Avalonia 走：LiveCharts 的 nuspec 钉的是 2.88.9，而 Avalonia 12 的 `Avalonia.Skia` 要求 `>= 3.119.3-preview.1.1`，两条一起进来就是 `NU1605` 包版本降级（本解决方案把警告当错误）。不声明 → 传递依赖取最高版（3.119.4），编译期照样能用 `SKColor` / `SolidColorPaint` |

工程是 `IsPackable=false` 的库：只服务于本解决方案，不发布 NuGet。
`RUSTool.UI` 只写 `ProjectReference`，不再自己钉 LiveCharts 版本 —— 图表栈的版本决策只有这一处。

## 7. 截图模式的一个口子

`RobotStatePanel.RedrawAll(Visual root)`（**静态**、**必须在 UI 线程调用**，返回重画的张数）：
让树里每张曲线图立刻按最新数据重画一次。

正常运行时用不上 —— 图表更新有自己的节流器（把一帧内的多次变更合批再画）；
而截图进程是「推完数据马上就要拍」，等不到那个计时器，不等的话 PNG 里拍到的是一圈空坐标轴。
做成静态方法（收一个可视树根）是因为截图模式下窗口刚布局完、面板实例还不好拿，
而「树里所有曲线图」正好是这个场景要的集合。用法见 `RUSTool.UI/Program.cs` 的 `--demo-torque`。

## 8. 怎么验证（不需要后端）

```bash
# 1) 六路关节力矩曲线：按协议的线格式真的拼满一屏（300 帧）状态帧，再走生产解码器解回来（只跳过 WebSocket）
RUSTool.UI/preview.sh torque                # -> preview/08-engineer-torque.png（浅色 · 工程师模式）

# 2) 空数据态与主题跟随：普通截图不推帧，曲线区撤掉、只留一句提示
RUSTool.UI/preview.sh dark                  # 深色：应看到「未连接控制通道 · 曲线待数据」

# 3) 一次拍全（共七张：四张界面组合 + 状态浮层 + 合成点云 + 合成力矩曲线）
RUSTool.UI/preview.sh all

# 4) 真实窗口里看曲线跟着合成帧长出来（不需要后端）
RUSTool.UI/preview.sh window --demo-torque
```

真实数据要连后端：`RUSTool.UI/preview.sh window` → 点「连接」→ 状态帧一到，六行开始从左往右长。

## 9. 约定与反例

**约定**

1. **行由调用方拥有**：控件只渲染你给的集合、并往行里推帧；不替你造行、也不换行。
2. **推帧只在 UI 线程**：改的是绑定源集合；跨线程推帧是数据竞争，不是「偶尔会抖」。
3. **单位在库外换算**：目录只描述「已经是什么」——避免「图上画的是度、读数写的是弧度」这种对不上。
4. **窗口长度只有一个来源**：`RobotStateRow.DefaultWindowFrames`（宿主 `TorqueChartViewModel.WindowFrames`
   转发它）—— 否则截图脚本推满一屏的帧数会和图上实际窗口对不上。
   卡片头上那句「15 s」是按**标称** 20 Hz 折出来的旁注（`NominalStateHz`）；横轴画的一直是帧序号，
   所以推帧快一点慢一点都不会让「窗口 300 帧」这句话变成假的。
5. **颜色只标识数据**：曲线色取自 `StatePalette`，不跟主题走；布局与文字色跟主题走。
6. **一行一路、行头不可切**：行在构造时就绑定了 `ChannelIndex`，没有「切通道」这个操作 ——
   于是也不存在「切过去之后历史接不接得上」这类状态（见 `../architecture/zh-CN.md` 的 ADR-020）。

**反例**

| 反例 | 为什么不行 |
|---|---|
| 在 `RUSTool.UI` 里 `using LiveChartsCore.*` / `SkiaSharp` | 隔离失效（本工程存在的全部理由）；图表库的版本变化会穿过界面层 |
| 推帧时不 `Post` 到 UI 线程 | `Values` 是绑定源集合，跨线程改会让图表与绑定同时读到半更新的状态 |
| 每帧换一个新的 `ObservableCollection` 实例 | 图表订阅的是集合实例，换实例等于让它重挂监听，还会丢掉这一帧的通知 |
| 对 `Values` 排序 / 过滤 | 曲线的 X 轴是帧序号，形状就是「时间上依次来到的值」；排序后形状不再是信号 |
| 按行号（第几行）决定曲线颜色 | 颜色回答的是「这是哪一路数据」，行号回答的是「第几行」—— 两者只是**现在**恰好同号；颜色该取 `ChannelIndex` |
| 行头再塞一个下拉框让用户切通道 | 那要维护「按通道分开的历史 + 切过去就地重填」两套状态，而卡片里一行一路本来就看全了六路（ADR-020） |
| 行头色标另写一份配色 | 色标与线必须是同一个色（`StatePalette.BrushFor` 与 `For` 同表）；写两份迟早分叉，分叉后色标指向的就不是那条线 |
| 把曲线色写进 `Theme/Tokens/Semantic.axaml` | 语义色是「控件状态」，换肤就该变；曲线色是「数据身份」，换肤不能变 |
| 直接把 `BridgeProtocol.StateFrame` 交给本工程 | 本工程只认识 `IReadOnlyList<double>`；协议模型一变，图表库不该跟着变 |
| 打开 `IsPackable` 去发布本工程 | 它是本解决方案内的隔离容器，不对外发布 |

---

后续要接的点（真实数据源、回放双轨联动、点云选点）见仓库根 [`../README.md`](../README.md) 第 7 节。

