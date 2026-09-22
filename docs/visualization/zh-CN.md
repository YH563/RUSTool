# RUSTool.Visualization（简体中文）

> 状态：反映当前实现。配套：[`../README.md`](../README.md)（仓库入口）、[`../architecture/zh-CN.md`](../architecture/zh-CN.md)、[`../ui/zh-CN.md`](../ui/zh-CN.md)、[`../testing/zh-CN.md`](../testing/zh-CN.md)。

这个工程只有一个职责：**让 3D 图形栈（Silk.NET + OpenGL + RobotSimulation）只在这里出现**。
`RUSTool.UI` 引用它，但只认识三个契约：`Controls/RobotViewport.cs`（控件）、
`Scene/PointCloudFrame.cs`（点云帧的形状）与
`Logging/ISimulationLogSink.cs`（日志出口，一个方法 + 四档枚举）。

---

## 1. 它解决什么问题

```
RUSTool.UI  ──绑定──►  RobotViewport.JointValues   （float 列表，单位弧度）
            ──调用──►  RobotViewport.SubmitPointCloud(PointCloudFrame)  （点云帧，任意线程）
                              │
                              ▼        ← 到此为止，界面的世界结束
                       RobotScene.Graph（场景图：网格 / 灯光 / 轴 / 机器人 / 点云图层）
                              │
                              ▼
                       RobotSimulation.*（Core / Robot / OpenGL）
```

**GL 的获取与 framebuffer 的绑定留在控件里，数据以普通值传进来** ——
界面层因此不需要任何 GL 类型、也不需要知道场景图长什么样：

| 关注点 | 归属 |
|---|---|
| GL 上下文 / framebuffer / 相机 / 拾取 / 每帧 | `RobotViewport`（本工程） |
| URDF 加载、关节驱动、模型装配报告、点云图层 | `RobotScene` / `PointCloudLayer`（本工程，纯 CPU） |
| 关节角数值 | `RUSTool.UI`（XAML 绑定 `JointValues`，弧度） |
| 点云帧（已反量化的数组） | `RUSTool.UI`（`Scene3DView` 把 Core 的帧适配成 `PointCloudFrame` 后投递） |
| 日志面板与落盘 | `RUSTool.UI`（实现 `ISimulationLogSink`） |

## 2. 数据契约（界面层需要知道的全部）

| 入口 | 类型 | 说明 |
|---|---|---|
| `JointValues` | `IReadOnlyList<float>?`（Avalonia 属性，可绑定） | 关节角，**弧度**，顺序 = URDF 可驱动关节顺序；长度不符则该帧被忽略并只提示一次 |
| `SubmitPointCloud(PointCloudFrame)` | 方法，**可从任意线程调用** | 一帧感知点云（`Xyz` 米 / `Rgb` 打包色）。覆盖式邮箱：上一帧还没画就被顶掉时记一次丢帧。数组按所有权交接、不复制 |
| `GizmoTopInset` | `double`（Avalonia 属性，可绑定） | 视口顶部被界面覆盖层占掉的高度（逻辑像素）：界面若在视口上叠了浮动层，填它的 `Bounds.Height` —— 覆盖层下面放不下库的最小 gizmo 方块（64 物理像素）时，右下角的朝向 gizmo 整块不画。0.3.0 起 gizmo 的**尺寸**归渲染器（视口短边 × 0.12，夹在 64~240 px），视口能决定的只剩「让不让位」，「把 gizmo 缩到覆盖层下面」那条老路已经不存在；`0`（默认）= 上方没有覆盖层。工程师工作区的状态读数是**按需展开**的右上角一角浮层（默认收起时视口上什么都没有，展开时也不碰右下角的 gizmo），所以那边不设这个属性；临床工作区右上角常驻的「末端接触力」浮层仍在用它 |
| `ResetCamera()` | 方法 | 相机回默认机位（菜单「视图 → 重置视角」用） |
| `Ready` / `Failed` | 事件（UI 线程） | GL 就绪报告（含 GPU 与模型装配报告）/ 初始化失败原因。失败时控件自己不画，请把它隐藏让占位层露出 |
| `Stats` / `Picked` | 事件（UI 线程） | 每秒性能行（FPS / 单帧耗时 / 帧数，收到点云时追加「点云 N 点 · seq …」）/ 单击拾取结果 |

单位约定与库一致：**弧度 / 米 / Z 轴向上**。界面这一层不做换算 ——
HUD 上给人看的度数是另一条投影（见 `RobotStatusViewModel`）。

界面**不自绘**朝向坐标轴：右下角那个 gizmo 是图形库画的（`RobotSimulation` 0.3.1 的
`SceneGraph.ShowOrientationGizmo`，默认开启，屏幕空间、不随相机缩放）。界面里出现第二条轴 = 重复。

界面也**不自己维护「选中」**：一次单击 = 库的 `SceneGraph.PickAndSelect` —— 命中就单选
（高亮 + 把该对象的局部坐标轴挂成它的普通子节点）、落空则清空。于是单击一个部件后除了变色，
还能看见**它自己的** X/Y/Z 朝哪：局部坐标轴是 `SceneGraph.ShowSelectionAxes` 的默认行为
（库默认开启，挂在节点下随它一起动，箭头尺寸恒定、不参与拾取，`SelectionAxesLength=0.3` 只是
箭头几何的参考长度），控件这边唯一的义务是走 `PickAndSelect` 而不是只翻高亮位的 `PickAndHighlight`。

相机手感（控件内常量，rviz 量级）：左键拖拽旋转 `0.2°/px`、中键平移 `0.01/px`、
右键拖拽与滚轮缩放；**按下与松开位移超过 6 像素才算拖拽**，否则算一次点击拾取。

## 3. 日志出口（图形栈 → 项目日志器）

库内部有自己的日志（URDF 资产解析、STL 网格导入、Assimp 原生库探测、模型装配），
走的是库的静态门面 `RobotSimulation.Core.Utils.Logger`（底层 Microsoft.Extensions.Logging）。
**这个门面在没人初始化时只建一个没有任何 provider 的工厂**：不接这一步，库日志写到哪里都不去。
接法是在组合根调一次：

```csharp
SimulationLogBridge.Attach(new SimulationLogSink(log));   // log 是 ILogService（App.CreateMainViewModel）
```

| 类型 | 位置 | 说明 |
|---|---|---|
| `ISimulationLogSink` / `SimulationLogLevel` | 本工程 `Logging/` | 界面层要实现的唯一接口（一个方法 + 四档枚举，**不认识** M.E.L 的 `LogLevel`） |
| `SimulationLogBridge` | 本工程 `Logging/` | 实现 `ILoggerProvider`/`ILogger` 把库日志转交出去；映射等级、拼异常、设置库的最低等级 |
| `SimulationLogSink` | `RUSTool.UI/Services/Logging/` | 界面侧实现：转手写进 `ILogService`（来源列 `sim` / `sim·模块名`） |

三条使用规则：

1. **越早越好**：库的 `Logger.Initialize` 只生效一次（第一个调用者决定 provider 集合），
   必须在任何库日志产生之前 `Attach`。本项目在 `App.CreateMainViewModel` 里、建视口之前做。
2. **可重复调用**：provider 每次读的是当前 sink，重复 `Attach` 只是换目标，不会重复注册。
3. **等级**：`Attach` 的第二个参数是库的最低等级（默认 `Debug`，即库的默认行为）；
   库内的 `Logger.IsEnabled` 也由它把关。

## 4. 工程内部结构

```
RUSTool.Visualization/
├── Controls/RobotViewport.cs        OpenGlControlBase 宿主：GL 生命周期 / 每帧 / 相机 / 拾取 /
│                                    关节帧与点云帧两条邮箱（唯一的数据入口）
├── Scene/RobotScene.cs              场景装配：默认场景 + URDF 模型 + 关节驱动 + 点云图层（纯 CPU）
├── Scene/PointCloudLayer.cs         感知点云图层：整帧替换 / 只读渲染侧的 CPU 数据
├── Scene/PointCloudFrame.cs         点云帧的形状（本工程自己的类型，不认识 RUSTool.Core）
├── Logging/
│   ├── ISimulationLogSink.cs    日志出口契约（1 个方法 + 4 档枚举）
│   └── SimulationLogBridge.cs   库的 ILoggerProvider → ISimulationLogSink 的桥
├── Assets/Models/               URDF + mesh（随编译复制到输出目录；见该目录 README）
└── README.md                    工程级说明（导航到本文）
```

`RobotScene` 是**加 3D 图层的落点**：点云已经挂上（`PointCloud`），规划路径、实时轨迹、力矢量箭头
今后往 `Graph` 上挂，上层界面不需要知道多了一个图层。它对外可见的状态：

| 成员 | 说明 |
|---|---|
| `Graph` | 场景图（库的构造函数已给好地面网格 / 灯光 / 相机位姿） |
| `Robot` / `ModelPath` | 实际加载的 `RobotModel` 与 URDF 绝对路径（没加载到就是 `null`） |
| `MachineBounds` | 零位姿态下的整机包围盒（米）；相机取景只看它，**不看点云**（点云可能比机械臂铺得大得多） |
| `LoadReport` | 装配过程的中文说明（加载了哪个模型、跳过了哪个、为什么）—— 界面可直接显示 |
| `PointCloud` | 感知点云图层（`PointCloudLayer`），装配时就挂上、默认不可见 |
| `ApplyJointValues(...)` | 关节角（弧度）→ URDF 关节的 `Transform`；渲染线程独占调用 |
| `ApplyPointCloudFrame(...)` | 一帧点云 → 点云图层（整帧替换）；同样只能在渲染线程调用 |

> **任何一步失败都不抛异常**：3D 面板不该因为一个数据文件（或缺显卡）而白屏 ——
> 失败原因写进 `LoadReport`，控件走 `Failed`，由界面决定显示什么。

### 4.0 感知点云图层（`/sensor`）怎么接进来的

数据从 WebSocket 线程来，而场景图从 RobotSimulation 0.3.0 起**归属主线程独占**
（`PointCloud2Data` 只能在属主线程上写）。整条链路因此被切成三段，每段只做一件事：

| 段 | 位置 | 线程 | 做什么 |
|---|---|---|---|
| 解码 | `RUSTool.Core` 的 `SensorFrameCodec` → `BridgeClient.SensorFrameReceived` | WS 线程 | 解压 + 反量化成 `float[]` / `uint[]` |
| 适配 + 投递 | `Scene3DView.OnPointCloudFrame` → `RobotViewport.SubmitPointCloud` | 仍是 WS 线程 | 换成 `PointCloudFrame`，写进视口的**一格邮箱**（覆盖式），并请求下一帧渲染 |
| 落地 | `RobotViewport.OnOpenGlRender` → `RobotScene.ApplyPointCloudFrame` → `PointCloudLayer.Apply` | 渲染线程（= 场景图属主） | `Clear()` + `EnsureCapacity()` + `AddPoints()` 整帧替换 |

几条刻意的取舍：

1. **整帧替换，不做增量**：协议的每帧都是自包含的完整点集（`scope=frame` 是当前帧、
   `scope=map` 是累积快照，都没有 delta 字段），所以复用同一份 `PointCloud2Data`
   （`Clear` + `AddPoints`）而不是 `SetData` 换一份新的。这样省下的是**重建**：容量不变时
   渲染器不重建 GPU 缓冲（`PointMesh.Sync` 只在 `Capacity` 变了才 `AllocateStore`）、
   CPU 侧不重新分配几个 MB；**上传量仍是整帧** —— 整帧替换让 `Dirty` 窗口正好覆盖全部点
   （`Clear` 归零、`AddPoints` 拉到 `count`），每帧上传 `count` 个点的位置 + 颜色
   （28 字节/点；30 万点 ≈ 8.4 MB/帧，15 fps 约 126 MB/s），协议不给 delta 就降不下去。
   转换用的 `Vector3[]` / `Vector4[]` 缓冲也只在点数变大时扩容，稳态零分配
   （每帧唯一的分配在解码侧：`float[]` / `uint[]`）。
2. **`PointCloud.MaxPoints` 保持 0**：它的语义是「超出上限就丢最老的」（滑窗），
   只作用于 `Append` / `AppendRange`；整帧替换不需要它，设了反而会悄悄砍掉地图快照的点。
3. **节点在装配时就挂上、默认 `Visible = false`**：场景图结构在整份生命期里不变，
   收到第一帧才显示 —— 结构不动最省事，也不会在渲染中途动到节点列表。
4. **邮箱只留一格（覆盖式）**：渲染跟不上推送时丢的是「还没来得及画的那几帧」，
   而不是积压 —— 与后端「只发最新一帧」的语义对齐。丢帧数进每秒那行诊断。
5. **重挂回来要立刻显示当前点云**：`OnOpenGlInit` 里用保留的最后一帧补一次
   （与关节值那条「重挂要显示当前关节角」同一理由），否则切换工作区后会空一块，
   直到下一帧感知数据到达。

> 上面的 4 / 5 两条与 `PointCloudLayer`、`RobotScene` 都是纯 CPU 逻辑，脱离 GL 也能读代码确认；
> 想看真实效果用 `preview.sh window --demo-cloud`（造一帧合成点云走完同一条链路，只跳过 WebSocket 传输）。

### 4.1 生命周期：GL 跟着【挂树期】生灭

控件的图形栈不是「构造即拥有」，而是**每次挂到可视树上重新建一遍**：

| 时机 | 发生什么 |
|---|---|
| `OnAttachedToVisualTree` | 记下 `_inputRoot` —— 指针 / 滚轮处理器挂在它上面，卸载时按这个引用精确摘除（卸载那一刻 `VisualRoot` 已经是 `null`，拿不到它）；再把当前关节角推一帧，恢复断链前的姿态 |
| `OnOpenGlInit` | 建 GL 上下文 / 场景图 / 相机。**所有「算一次就存下来的」状态必须在这里清空**：像素视口与缩放比、gizmo 布局、关节数不符时只提示一次的那面旗、FPS 累加器与上一帧时刻，并重启帧时钟 —— 留着的话，第二块 framebuffer 会继承上一块的数字，画面比例与提示节奏都是错的；同一处还会把**最后一帧点云**补进新场景（点云图层是随场景图一起新建的，不补就空一块，直到下一帧感知数据到达） |
| `OnDetachedFromVisualTree` | 摘掉上面那四个窗口级处理器，并把拖拽标志归零 —— 拖拽是「视口还在树上」时的状态，留着会让重挂后的第一下点击被当成上一次拖拽的延续 |
| `OnOpenGlDeinit` | 释放 GPU 资源（库的 `GraphicsContext` 与场景图持有的一切），日志出一行 `Viewport: 已释放 GL 资源` |

**推论（本工程最重要的一条约定）**：要让界面「隐藏」一个视口，必须把它从树上**摘下来**，
不能只把 `IsVisible` 设成 `false`。后者只是不参与合成 —— GL 上下文、URDF/STL 模型、
每帧请求下一帧的渲染循环全都还在；两个视口同时活着 = 两条渲染循环交替出图，
表现就是坐标系与末端法兰**持续频闪**（本机实测）。宿主侧怎么摘的见
[`../ui/zh-CN.md`](../ui/zh-CN.md) 第 9.5 节。代价是切回来要重建一次图形栈：
日志里每次切换都会多一行 `[3d] 就绪 …`，属正常，不是异常。

## 5. 依赖

| 包 | 版本 | 作用 |
|---|---|---|
| `RobotSimulation.Core` / `.Robot` / `.OpenGL` | 0.3.1 | 场景图 / URDF + 正运动学 / Silk.NET 渲染后端（0.2.0 起自带屏幕空间朝向 gizmo；0.2.1 修正关节合成顺序；**0.3.0** 把 gizmo 尺寸改归渲染器、给场景图立下线程契约 —— 属主线程只声明一次，`PointCloud2Data` 只能由属主线程写；0.3.1 补 `Axes.AlwaysOnTop` / `GameObject.ShowLocalAxes`） |
| `Microsoft.Extensions.Logging` | 10.0.11 | `SimulationLogBridge` 实现 `ILoggerProvider` / `ILogger` 用；用 net8.0 资产，不给项目带进任何 10.0 运行时 |
| `Avalonia` | 12.1.0 | `OpenGlControlBase`：给我们一个 GL 上下文和一个 framebuffer |
| `Silk.NET.OpenGL` | 2.23.0 | 把 Avalonia 的过程地址包成 GL 门面 |

**要求桌面 OpenGL**：后端只带 `#version 330 core` 着色器，拿到 GLES（部分平台的 ANGLE / EGL 默认）
会抛 `NotSupportedException` —— 控件会捕获它并走 `Failed` 降级，不会让应用崩掉。

包全部来自 **nuget.org**（`RobotSimulation` 0.1.0 / 0.2.0 / 0.2.1 / 0.3.0 / 0.3.1 都已发布），
仓库根的 `NuGet.config` 只登记这一个源 —— 换机器 / 上 CI 不需要任何手工加源。
真无外网时只能靠 `~/.nuget/packages` 里已有的缓存还原，本仓库不再依赖本机离线目录。

## 6. 资产

`Assets/Models/` 随编译复制到输出目录，并被引用本工程的 `RUSTool.UI` 一并带上，
所以加载路径固定写 `AppContext.BaseDirectory/Assets/...`，与启动时的工作目录无关。见 `Assets/Models/README.md`。

## 7. 怎么验证（不需要后端）

```bash
# 1) 界面 + GL：真窗口跑起来，stderr 会出现 [3d] 就绪 …（含 GPU 与模型加载报告）
RUSTool.UI/preview.sh window

# 2) 无 GL 时的降级：离屏截图不崩、3D 区显示设计好的空状态
RUSTool.UI/preview.sh dark

# 3) 图形栈日志确实回到了项目日志器（来源列 sim，含库内模块名）
grep ' sim ' logs/$(date +%F).log        # 例：sim [AssetResolver] 'package://…' → …

# 4) 工作区来回切换（菜单「视图 → 临床模式 / 工程师模式」）：
#    每次切换都该是「先释放、再重新就绪」，绝不该出现两个视口并存
RUSTool.UI/preview.sh window   # 依次看到：已释放 GL 资源 → 重新加载 base_link.STL → [3d] 就绪 …
```

---

## 8. 约定与反例

**约定**

1. **单位只认库的那一套**：弧度 / 米 / Z 轴向上；界面要显示度数就在 VM 里换算（HUD 那条投影）。
2. **数据只经「邮箱」跨线程传递**：UI / WS 线程只写一格（关节值一个、点云帧一个），
   渲染线程取走即置空 —— 界面线程绝不跨线程碰场景对象（`SceneGraph` 归渲染线程独占）。
   点云那一格是**覆盖式**的：没画完就被顶掉的老帧计入丢帧数，而不是排队等渲染。
3. **越早挂日志桥**：`SimulationLogBridge.Attach` 必须在任何库日志产生之前调用，
   本项目在 `App.CreateMainViewModel` 里、建视口**之前**做。
4. **失败一律降级**：缺模型 / 缺桌面 GL / GLES 上下文都不许白屏，走 `LoadReport` + `Failed`。
5. **单击 = 库的 `PickAndSelect`**：高亮与「选中对象的局部坐标轴」由库在同一步里管好（`Select`），
   控件不另存一份选中状态 —— 拾取只负责把点变成射线。

**反例**

| 反例 | 为什么不行 |
|---|---|
| 在 `RUSTool.UI` 里 `using Silk.NET.*` / `RobotSimulation.*` | 图形栈会泄漏进界面层，隔离失效（本工程存在的全部理由） |
| 界面自己画朝向坐标轴 | 与库自带的 gizmo 重复；gizmo 由 `SceneGraph.ShowOrientationGizmo` 统一控制 |
| 控件自己记一份「当前选中」 | 与库的 `SceneGraph.Selected` 平行，只会让一半生效：用 `PickAndHighlight` 自己复原高亮时，选中对象的局部坐标轴永远挂不上（单击只变色，看不出它的 X/Y/Z 朝哪） |
| 两个 3D 视口都挂在树上、用 `IsVisible` 藏一个 | `IsVisible=false` 不释放 GL：两套上下文 / 两份模型 / 两条渲染循环同时活着，切换后画面在两者之间交替（坐标系与末端法兰频闪）。正确做法是只把当前模式那一份挂进宿主（`MainWindow` 的 `WorkspaceHost`），另一份从树上摘除 —— 见第 4.1 节 |
| 在 UI 线程里改 `RobotScene.Graph` | 与渲染线程竞争；只能经 `JointValues` / `SubmitPointCloud` 邮箱传数据 |
| WS 线程里直接写 `PointCloud2Data`（或 `Apply` 点云图层） | RobotSimulation 0.3.0 起场景图与它的点云数据都只归属主线程；跨线程写就是数据竞争，正确入口只有视口的邮箱 |
| 把点云帧绑到 XAML 属性上（像 `JointValues` 那样） | 每秒十几帧、每帧几 MB：每次赋值都会触发通知与布局，且数据在 UI 线程上多绕一圈 —— 点云走「VM 事件 → 视口方法」 |
| 给点云设 `MaxPoints` 当滑窗用 | 协议每帧都是完整点集（没有 delta），滑窗语义用不上；设了反而会悄悄砍掉地图快照的点 |
| 界面把 `JointValues` 当角度（度）传进去 | 模型关节会明显乱动 —— 单位是弧度 |
| 直接引用库的 `Logger` 写日志 | 库日志门面只认第一次初始化，应由 `SimulationLogBridge` 统一接出 |
| 把 `IsPackable` 打开去发布本工程 | 它是本解决方案内的隔离容器，不对外发布（`IsPackable=false`） |
