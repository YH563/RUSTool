using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using Avalonia.Threading;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Utils;
using RobotSimulation.OpenGL.Device;
using RobotSimulation.OpenGL.Rendering;
using RUSTool.Visualization.Scene;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;

namespace RUSTool.Visualization.Controls;

/// <summary>
/// 内嵌 3D 视口 —— 整个界面层唯一需要认识的图形类型。
///
/// <para>
/// 它的存在意义是把三件事**关在这一个文件里**，让 RUSTool.UI 的其余部分（乃至 RUSTool.Core）
/// 永远不需要知道 GL 的存在：
/// </para>
/// <list type="number">
/// <item><b>GL 从哪来</b>：Avalonia 给每个 <see cref="OpenGlControlBase"/> 一个上下文，本类是它的组装点。</item>
/// <item><b>画到哪去</b>：每帧绑定控件自己的 framebuffer、向它实测同步视口、渲染、请求下一帧。</item>
/// <item><b>指针怎么变成相机</b>：rviz 风格左键旋转 / 中键平移 / 右键缩放 / 单击拾取选中。</item>
/// </list>
///
/// <para>
/// 视口的生命期等于**挂树期**：控件离开可视树时 Avalonia 回调 <see cref="OnOpenGlDeinit"/>，
/// GL 资源在那里释放，再挂回来会重新初始化一遍。所以本类里所有「上次算过就不用再算」的缓存
/// 都在 <see cref="OnOpenGlInit"/> 里清零 —— 它们描的是上一份场景图与上一份 framebuffer。
/// 界面把视口【摘下来换另一个】而不是把它藏起来（<c>IsVisible=false</c>），要的正是这个释放：
/// 两个 GL 视口并存会让画面持续频闪（见 <c>MainWindow.axaml.cs</c>）。
/// </para>
/// <para>
/// 数据入口只有一个 <see cref="JointValues"/>（纯 float 列表，单位弧度）——界面把 <c>/state</c>
/// 的关节角喂进来，模型就动。这个属性上不出现任何 RobotSimulation / Silk.NET 类型，
/// 因此 RUSTool.UI 可以在不引用图形栈的前提下绑定它，也仍然能被离屏截图模式渲染。
/// </para>
/// <para>
/// 数据之外还有一格布局入口 <see cref="GizmoTopInset"/>：界面的视口上**可以**叠浮动层（状态 HUD、
/// 工具条），而库把朝向 gizmo 钉在**右下角**、位置不可调 —— 界面把浮动层的实测高度递进来，
/// 视口自己把 gizmo 缩到它下面，于是"覆盖层压住罗盘"这件事不需要界面去猜 gizmo 的算法。
/// 界面不叠任何东西、或叠的东西**默认不存在**（工程师工作区右上角那块按需展开的状态浮层就是后者）
/// 时不必设它，走库的默认尺寸；临床工作区右上角常驻的「末端接触力」浮层仍靠它给 gizmo 让位。
/// </para>
/// <para>
/// 单击之后画什么同样不由本类决定：高亮（<c>GameObject.Highlighted</c>）与挂在被选节点下的
/// 局部坐标轴（<c>SceneGraph.ShowSelectionAxes</c>，库默认开启）都是库的显示行为，
/// 本类只把「点在哪」变成一条射线。选中状态只有一份、归场景图所有，见 <see cref="PickAt"/>。
/// </para>
/// <para>
/// 初始化失败（无显卡、无桌面 GL、驱动不认）时本控件**不抛异常、不白屏**：
/// 记下原因、把 <see cref="Failed"/> 交给界面、此后不再绘制 —— 叠在它下面的占位内容自然露出。
/// </para>
/// </summary>
public sealed class RobotViewport : OpenGlControlBase
{
    // ── 相机手感（rviz 默认值的量级）──
    private const float RotateDegreesPerPixel = 0.2f;
    private const float PanScale = 0.01f;
    private const float ZoomPerDragPixel = 0.03f;
    private const float ZoomPerScrollNotch = 0.5f;

    /// <summary>按下与松开之间的位移超过它就判定为「拖拽」而非「单击拾取」（屏幕像素）。</summary>
    private const double ClickDragThresholdPixels = 6.0;

    /// <summary>性能行的间隔（秒）。</summary>
    private const double StatsIntervalSeconds = 1.0;

    // ── 朝向 gizmo 的布局（见 ApplyGizmoLayout）──
    /// <summary>库的默认边长（逻辑像素）—— 上方没有覆盖层时的上限。</summary>
    private const double GizmoMaxSize = 160;

    /// <summary>再挤也不小于这个边长（逻辑像素），否则方向读不出来。</summary>
    private const double GizmoMinSize = 56;

    /// <summary>
    /// 与视口右下边缘的间隙（逻辑像素）。库的默认是 16，这里取 12 —— 和卡片里其他角标（左下角
    /// 那行状态文字的左右内衬）对齐，同一块画布里不留两种边距。
    /// </summary>
    private const double GizmoEdgeMargin = 12;

    /// <summary>与顶部覆盖层之间的间隙（逻辑像素）：贴着画会被它的阴影压住。</summary>
    private const double GizmoTopGap = 8;

    // ── 渲染状态：只在 GL 回调里存在，随上下文一起生灭 ──
    private GL? _gl;
    private IRenderContext? _graphics;
    private IRenderer? _renderer;
    private RobotScene? _robotScene;

    /// <summary>上一次算 gizmo 尺寸用到的输入（视口高 / 缩放 / 覆盖层占位）：输入没变就不重算。</summary>
    private (int PixelHeight, double Scaling, double TopInset) _gizmoLayout = (-1, -1, -1);

    // ── 关节帧邮箱：UI 线程只写这一格，渲染线程取走即置空（同一帧不会被重复应用）──
    private readonly object _jointsLock = new();
    private float[]? _pendingJoints;
    private bool _jointMismatchReported;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastFrameTime;
    private double _statsAccumulator;

    // ── 指针状态：事件挂在窗口上（见 OnAttachedToVisualTree）──
    /// <summary>
    /// 指针处理器挂在哪个根上（挂树那一刻记下来）。摘树时用它精确摘钩、而不是再看
    /// <see cref="VisualRoot"/>：那一刻它可能已经为空，漏摘就会把视口连同它的处理器一起留在窗口上
    /// —— 而切换工作区（视口反复挂 / 摘）正是最容易踩到这条路径的场景。
    /// </summary>
    private InputElement? _inputRoot;

    private bool _dragging;
    private bool _dragged;
    private Point _pressPosition;

    /// <summary>
    /// 按下那一刻的机位。单击拾取必须打在用户看到的那一帧画面上，见 OnWindowPointerReleased。
    /// </summary>
    private CameraPose _pressCamera;

    /// <summary>
    /// 本帧实际画进的像素矩形（向 GL 表面量得，见 <see cref="SyncViewport"/>）。
    /// 渲染视口、相机宽高比、拾取射线三者共用它 —— 只有一把尺，三者就不会各说各话。
    /// </summary>
    private (int Width, int Height) _pixelViewport;

    /// <summary><see cref="_pixelViewport"/> 的来源（诊断用，视口那行日志会写明）。</summary>
    private string _viewportSource = "尚未测量";

    /// <summary>视口尺寸只在第一次量到时记一行日志，不刷屏。</summary>
    private bool _viewportReported;

    /// <summary>
    /// 关节角（弧度），顺序与 URDF 的可驱动关节一致。
    /// 绑定即可驱动模型；长度与模型不符时该帧被忽略（且只提示一次，不刷屏）。
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<float>?> JointValuesProperty =
        AvaloniaProperty.Register<RobotViewport, IReadOnlyList<float>?>(nameof(JointValues));

    /// <inheritdoc cref="JointValuesProperty"/>
    public IReadOnlyList<float>? JointValues
    {
        get => GetValue(JointValuesProperty);
        set => SetValue(JointValuesProperty, value);
    }

    /// <summary>
    /// 视口顶部被界面覆盖层占掉的高度（逻辑像素）—— 例如 3D 卡片右上角那块「机械臂状态」HUD。
    ///
    /// <para>
    /// 库把朝向 gizmo 钉在视口右下角、位置不可调，而覆盖层是从上往下压的：两者抢同一块地方时，
    /// 能让路的只有 gizmo 的尺寸。填上覆盖层的实测高度即可（绑它的 <c>Bounds.Height</c>），
    /// gizmo 会自动收在这条线下面；<c>0</c>（默认）＝ 上方没有覆盖层，用库的默认尺寸。
    /// </para>
    /// </summary>
    public static readonly StyledProperty<double> GizmoTopInsetProperty =
        AvaloniaProperty.Register<RobotViewport, double>(nameof(GizmoTopInset));

    /// <inheritdoc cref="GizmoTopInsetProperty"/>
    public double GizmoTopInset
    {
        get => GetValue(GizmoTopInsetProperty);
        set => SetValue(GizmoTopInsetProperty, value);
    }

    /// <summary>GL 初始化完成（UI 线程）：携带 GPU / 版本 / 模型装配报告，供界面显示或记日志。</summary>
    public event Action<string>? Ready;

    /// <summary>GL 初始化失败（UI 线程）：携带原因。收到后应把控件隐藏，让占位内容露出。</summary>
    public event Action<string>? Failed;

    /// <summary>每秒一行性能信息（UI 线程）：FPS / 单帧耗时 / 累计帧数。</summary>
    public event Action<string>? Stats;

    /// <summary>拾取结果（UI 线程）：单击命中的节点名，或「未命中」与长度不匹配之类的提示。</summary>
    public event Action<string>? Picked;

    /// <summary>把相机还给「默认机位 + 对准整机」（与场景刚建好时一致）。</summary>
    public void ResetCamera()
    {
        if (_robotScene is null)
            return;

        _robotScene.ResetView();
        RequestNextFrameRendering();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == JointValuesProperty)
            OnJointValuesChanged();
    }

    /// <summary>
    /// 把绑定进来的关节值拷成私有快照放进邮箱。
    /// 必须拷贝：绑定源下一帧就换新数组，而渲染线程可能还在读旧的；
    /// 交换的是引用，所以写入侧不需要等待渲染线程。
    /// </summary>
    private void OnJointValuesChanged()
    {
        if (JointValues is not { Count: > 0 } values)
            return;

        var snapshot = new float[values.Count];
        for (int i = 0; i < snapshot.Length; i++)
            snapshot[i] = values[i];

        lock (_jointsLock)
            _pendingJoints = snapshot;

        RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        // 每份 GL 生命期都从零开始算。控件会「摘下来再挂回去」—— 切换工作区走的就是这条路径
        // （见 MainWindow.axaml.cs）—— 而那上面那份 framebuffer / 场景图 / gizmo 尺寸都已经不存在了。
        // 这些「上次算过就不用再算」的缓存留着旧值，就是拿上一份视口的数据算这一份的画面：
        // 尺寸恰好没变时 ApplyGizmoLayout 会以为已经算过而直接返回，gizmo 于是停在库的默认尺寸上、
        // 不再为覆盖层让位。所以初始化这一侧先把它们清干净。
        _pixelViewport = default;
        _viewportSource = "尚未测量";
        _viewportReported = false;
        _gizmoLayout = (-1, -1, -1);
        _jointMismatchReported = false;
        _statsAccumulator = 0;
        _lastFrameTime = default;
        _clock.Restart();

        try
        {
            // Avalonia 只给过程地址；用 Silk 门面包一层，就是「怎么拿到 GL」的全部差异。
            // 这个 GL 实例此后不出本类。
            _gl = GL.GetApi(gl.GetProcAddress);

            // 后端只带桌面 GL 着色器（#version 330 core）。ES 上下文（部分平台 ANGLE / EGL 的默认）
            // 会在很深的着色器编译处才炸，这里提前把话说清楚。
            if (GlVersion.Type == GlProfileType.OpenGLES)
                throw new NotSupportedException($"需要桌面 OpenGL 上下文，实际拿到 GLES（{GlVersion}）");

            (_graphics, _renderer) = GraphicsFactory.Create(_gl);

            GraphicsDeviceInfo device = _graphics.DeviceInfo;

            // 场景装配在这里完成：默认 SceneGraph（网格 / 灯光 / 世界轴 / 可用机位）+ 机器人模型。
            _robotScene = RobotScene.CreateDefault();

            // 库的默认机位按「几米见方」的场景设计，这里按整机包围盒重新取景（同 ResetView）。
            _robotScene.ResetView();

            string report = $"GPU {device.Renderer} · GL {device.ApiVersion} · {_robotScene.LoadReport}";
            Dispatcher.UIThread.Post(() => Ready?.Invoke(report));
        }
        catch (Exception ex)
        {
            // 初始化失败绝不能把整个应用带走：记住原因 → 报告 → 此后不再绘制。
            string reason = ex.Message;
            Dispatcher.UIThread.Post(() => Failed?.Invoke(reason));
        }
    }

    protected override void OnOpenGlRender(GlInterface gl, int framebuffer)
    {
        if (_gl is null || _graphics is null || _renderer is null || _robotScene is null)
            return; // 初始化失败（原因已报过），没有可画的东西。

        // Avalonia 要求画进本控件自己的 framebuffer；画进默认的 0 号永远到不了屏幕。
        // 库从不自己绑定 framebuffer —— 这正是它能被内嵌的原因。
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, (uint)framebuffer);

        // 视口不按控件尺寸「算」，而是向 GL 表面「量」（见 SyncViewport）：
        // 合成器给这张表面多少像素是它说了算，界面按 布局 × RenderScaling 去猜可以差几个百分点，
        // 而画面与拾取射线必须落在同一个矩形里，否则点得越靠边偏得越多。
        (int pixelWidth, int pixelHeight) = SyncViewport();

        SceneGraph scene = _robotScene.Graph;

        // 覆盖层（界面若在视口上叠了浮动层）占掉的高度由界面递进来 —— 借这一步把 gizmo 收在它下面；
        // 界面不叠东西时这个值是 0，gizmo 就用库的默认尺寸。
        // 缩放因子同样取量出来的「布局单位 → 物理像素」实际比例：gizmo 与画面同处一个像素网格，
        // 界面的逻辑量（覆盖层高度、边距）才不会在分数缩放下与画面错位。
        ApplyGizmoLayout(pixelHeight, PixelsPerLayoutUnit.Y, scene);
        scene.Camera.AspectRatio = pixelWidth / (float)pixelHeight;

        // 取走本帧的关节值（取走即置空）。场景图归渲染线程独占，UI 线程只通过邮箱递数据。
        float[]? joints;
        lock (_jointsLock)
        {
            joints = _pendingJoints;
            _pendingJoints = null;
        }

        if (joints is not null && !_robotScene.ApplyJointValues(joints) && !_jointMismatchReported)
        {
            _jointMismatchReported = true;
            string message = $"关节值长度与模型不符（收到 {joints.Length}，模型需要 " +
                             $"{_robotScene.Robot?.DrivableJointCount ?? 0}），已忽略";
            Dispatcher.UIThread.Post(() => Picked?.Invoke(message));
        }

        TimeSpan now = _clock.Elapsed;
        double deltaSeconds = Math.Clamp((now - _lastFrameTime).TotalSeconds, 0, 0.25);
        _lastFrameTime = now;

        scene.Update(deltaSeconds);              // 关节已写入 Transform，这里逐帧派发
        _graphics.Clear(scene.BackgroundColor);  // Clear 会先铺场景背景色
        _renderer.Render(scene);

        // 不请求下一帧，画面就停在第一帧。
        RequestNextFrameRendering();

        // 每秒一行性能信息；统计数据是纯数据，在渲染线程读是安全的。
        FrameStats stats = _renderer.Stats;
        _statsAccumulator += deltaSeconds;
        if (_statsAccumulator >= StatsIntervalSeconds)
        {
            _statsAccumulator = 0;
            string line = $"{stats.Fps:F1} FPS · 单帧 {stats.LastFrameMilliseconds:F2} ms · " +
                          $"平均 {stats.AverageFrameMilliseconds:F2} ms · 累计 {stats.FrameCount} 帧";
            Dispatcher.UIThread.Post(() => Stats?.Invoke(line));
        }
    }

    /// <summary>
    /// 给朝向 gizmo 定尺寸：让它整块待在「顶部覆盖层下沿」以下。
    ///
    /// <para>
    /// 算的是覆盖层下面还剩多大的正方形 —— <c>视口高 − 覆盖层占位 − 间隙 − 下边距</c>，
    /// 再夹在 [<see cref="GizmoMinSize"/>, <see cref="GizmoMaxSize"/>] 之间。取值保守到把 gizmo 的
    /// 整个正方形框进去（库的正交视野是箭头的 2.2 倍，罗盘只占正方形的 0.95，剩下的余量留给箭头端点），
    /// 所以宁可小一点也不要贴上去。
    /// </para>
    /// <para>
    /// 单位：库按【物理像素】量 gizmo（它与渲染面同一把尺），界面按【逻辑像素】量覆盖层，
    /// 因此这里把逻辑量乘上 <c>RenderScaling</c> —— 于是 HiDPI 屏上 gizmo 的逻辑尺寸既不缩水，
    /// 也不会因为覆盖层换算后看着更小就反过来放大到撞上 HUD。缩放为 1 时两把尺一样长，
    /// 交给库的就是上面这些逻辑值本身（只有边距按本控件的 12，而不是库的 16）。
    /// </para>
    /// </summary>
    private void ApplyGizmoLayout(int pixelHeight, double scaling, SceneGraph scene)
    {
        double inset = Math.Max(0d, GizmoTopInset);

        // 输入没变（绝大多数帧都走这条）→ 不碰场景图。
        if (_gizmoLayout == (pixelHeight, scaling, inset))
            return;

        _gizmoLayout = (pixelHeight, scaling, inset);

        float margin = (float)Math.Round(GizmoEdgeMargin * scaling);
        float cap = (float)(GizmoMaxSize * scaling);
        float floor = (float)(GizmoMinSize * scaling);

        // 从上往下逐段扣：覆盖层 → 间隙 → 下边距；剩下的高度就是正方形边长。
        var available = (float)(pixelHeight - (inset + GizmoTopGap) * scaling - margin);
        float size = Math.Clamp(available, floor, cap);

        scene.OrientationGizmoMargin = margin;
        scene.OrientationGizmoSize = size;
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        // 上下文还在当前线程时释放 GPU 资源，最后才丢 GL 门面。
        _renderer?.Dispose();
        _robotScene?.Dispose();
        _graphics?.Dispose();
        _gl?.Dispose();

        _renderer = null;
        _robotScene = null;
        _graphics = null;
        _gl = null;

        // 只有视口真的离开可视树（切换工作区 / 关窗口）才会走到这里。记一行是为了让
        // 「同一时刻只有一个视口」在日志里可查：切换前后应当看到「就绪 / 已释放」成对出现，
        // 而不是两份「就绪」都还活着 —— 后者就是两个 GL 视口并存、画面频闪的现场。
        Logger.Info("Viewport: 已释放 GL 资源（视口离开可视树，同一时刻只保留一份）");
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // GL 控件的内容会被合成器当作一张独立表面，命中测试直接穿到父级 ——
        // 控件自己收不到指针事件。所以监听窗口，再用本控件的矩形过滤。
        _inputRoot = VisualRoot as InputElement;
        if (_inputRoot is { } root)
        {
            root.AddHandler(PointerPressedEvent, OnWindowPointerPressed,
                RoutingStrategies.Bubble, handledEventsToo: true);
            root.AddHandler(PointerMovedEvent, OnWindowPointerMoved,
                RoutingStrategies.Bubble, handledEventsToo: true);
            root.AddHandler(PointerReleasedEvent, OnWindowPointerReleased,
                RoutingStrategies.Bubble, handledEventsToo: true);
            root.AddHandler(PointerWheelChangedEvent, OnWindowPointerWheel,
                RoutingStrategies.Bubble, handledEventsToo: true);
        }

        // 重挂回来的视口要立刻显示【当前】关节角：邮箱在上一份生命期里被取走后是空的，
        // 而下一帧状态未必马上到（未连后端时根本不会来），模型就会停在库的默认姿态上。
        OnJointValuesChanged();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_inputRoot is { } root)
        {
            root.RemoveHandler(PointerPressedEvent, OnWindowPointerPressed);
            root.RemoveHandler(PointerMovedEvent, OnWindowPointerMoved);
            root.RemoveHandler(PointerReleasedEvent, OnWindowPointerReleased);
            root.RemoveHandler(PointerWheelChangedEvent, OnWindowPointerWheel);
            _inputRoot = null;
        }

        // 拖拽是「视口还在树上」时的状态：摘下来的这一刻它已经没有意义，
        // 留着会让重挂后的第一下点击被当成上一次拖拽的延续。
        _dragging = false;
        _dragged = false;

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>指针是否落在本控件矩形内（窗口级处理器会收到整个窗口的事件）。</summary>
    private bool IsPointerInside(PointerEventArgs e)
    {
        Point position = e.GetPosition(this);
        return position.X >= 0 && position.Y >= 0
            && position.X <= Bounds.Width && position.Y <= Bounds.Height;
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_robotScene is null || !IsPointerInside(e))
            return;

        _dragging = true;
        _dragged = false;
        _pressPosition = e.GetPosition(this);
        _pressCamera = CameraPose.Capture(_robotScene.Graph.Camera); // 单击必须留住按下这一刻的机位
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || _robotScene is null)
            return;

        Point position = e.GetPosition(this);

        // 阈值内的位移（普通单击时指针的抖动）故意不动相机：拾取要用的正是用户看到的那一帧机位，
        // 只有明确的拖拽才允许改它。
        if (!_dragged && Distance(position, _pressPosition) > ClickDragThresholdPixels)
            _dragged = true; // 明确的拖拽发生了 → 松手时不能再当单击拾取

        if (!_dragged)
            return;

        // 拖拽按【相对按下点的总位移】施加在按下时的机位上，而不是逐帧增量：
        // 越过阈值的那一帧相机不会跳一下，取整误差也不会累积成「越拖越偏光标」。
        var total = new Vector2(
            (float)(position.X - _pressPosition.X),
            (float)(position.Y - _pressPosition.Y));

        Camera camera = _robotScene.Graph.Camera;
        _pressCamera.Restore(camera);

        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
            // 屏幕 Y 向下、相机俯仰向上：不取反的话拖拽方向是反的。
            camera.Rotate(-total.X * RotateDegreesPerPixel, -total.Y * RotateDegreesPerPixel);
        else if (point.Properties.IsMiddleButtonPressed)
            camera.Pan(total * PanScale);
        else if (point.Properties.IsRightButtonPressed)
            camera.Zoom(-total.Y * ZoomPerDragPixel); // 向上拖（dy<0）＝拉近

        RequestNextFrameRendering();
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;

        // 单击选中、拖拽只转相机。松手时先把机位放回按下那一刻：指针在阈值内仍可能抖动一两个像素、
        // 抬起前的一小段移动也已经把相机转了一点（约 1°），而拾取只能打在用户点下去的那帧画面上，
        // 否则射线会从部件旁边擦过去 —— 表现就是「明明点在机械臂上却没选中」。
        if (!_dragged && _robotScene is not null && IsPointerInside(e))
        {
            _pressCamera.Restore(_robotScene.Graph.Camera);
            PickAt(e.GetPosition(this));
        }
    }

    private void OnWindowPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_robotScene is null || !IsPointerInside(e))
            return;

        _robotScene.Graph.Camera.Zoom((float)e.Delta.Y * ZoomPerScrollNotch);
        RequestNextFrameRendering();
        e.Handled = true;
    }

    /// <summary>
    /// 屏幕像素 → 世界射线 → 拾取并选中（高亮 + 该节点自身坐标系）。相机是纯 CPU 数据，这条射线投射完全不碰 GL。
    ///
    /// <para>
    /// 单选整条走库的 <c>SceneGraph.PickAndSelect</c>：它内部调 <c>Select</c>，命中就把新对象设为
    /// <c>Selected</c>、让上一个回到原样、并把新对象的局部坐标轴挂成它的一个普通子节点
    /// （<c>ShowSelectionAxes</c>，库默认开启）；落空则清空选中。
    /// </para>
    /// <para>
    /// 以前这里用的是 <c>PickAndHighlight</c>（只翻高亮位），于是本类不得不自己记「当前选中」替库去
    /// 复原上一个对象的高亮 —— 一份平行状态，代价是选中只在高亮这一半上生效：局部坐标轴永远等不到
    /// <c>Select</c> 那一步，单击选中的对象看不出自己的 X/Y/Z 朝哪。
    /// </para>
    /// </summary>
    private void PickAt(Point position)
    {
        GameObject? picked = _robotScene!.Graph.PickAndSelect(RayAt(position));

        string message = picked is null ? "单击拾取：未命中" : $"单击拾取：{picked.Name}";
        Dispatcher.UIThread.Post(() => Picked?.Invoke(message));
        RequestNextFrameRendering();
    }

    /// <summary>
    /// 本控件【布局坐标】下的一点对应的拾取射线 —— 也就是 <c>e.GetPosition(this)</c> 给出的那个空间，
    /// 指针、布局与 framebuffer 只有在这里才能对上账。
    ///
    /// <para>
    /// 两次换算（布局单位 → framebuffer 像素、像素 → NDC）都只在本方法里发生，
    /// 而矩形与比例取自同一处 <see cref="PixelViewport"/> / <see cref="PixelsPerLayoutUnit"/>，
    /// 所以点击所依据的矩形和画面所占据的矩形不可能不一致。
    /// </para>
    /// </summary>
    private Ray RayAt(Point position)
    {
        SceneGraph scene = _robotScene!.Graph;
        (int width, int height) = PixelViewport;
        Vector2 scale = PixelsPerLayoutUnit;

        // 拾取可能落在「刚 resize、下一帧还没画」之间；宽高比按同一组数字刷新，
        // 射线就始终与它要穿过的那个画面同步。
        scene.Camera.AspectRatio = width / (float)height;

        return scene.Camera.ScreenToWorldRay(
            new Vector2((float)(position.X * scale.X), (float)(position.Y * scale.Y)),
            new Vector2(width, height));
    }

    /// <summary>
    /// 把视口对齐到 GL 实际给出的表面，并返回量到的像素矩形。
    ///
    /// <para>
    /// 要点在「量」而不是「算」：本控件布局多大是界面的事，合成器给这张表面多少像素是它的事，
    /// 两者在分数显示的缩放上可以差几个百分点（本机实测：1100×718 的逻辑尺寸拿到 1157×755 的表面）。
    /// 谁按 <c>Bounds × RenderScaling</c> 去猜，画面就只铺满那张表面的一部分、指针却按整块算 ——
    /// 表现为点得越靠边偏得越多，而拾取总是擦过部件。渲染、相机宽高比、拾取射线从此共用这一个矩形。
    /// </para>
    /// </summary>
    private (int Width, int Height) SyncViewport()
    {
        (int width, int height) = _gl is { } gl ? QueryFramebufferSize(gl) : default;

        if (width <= 0 || height <= 0)
        {
            // 拿不到可量的表面（首帧之前、或驱动藏起了附着）→ 按布局预测，
            // 下一个尺寸一开始渲染就会被真实值取代。
            (width, height) = LayoutPixelSize();
            _viewportSource = "布局预测";
        }
        else
        {
            _viewportSource = "GL 表面实测";
        }

        if ((width, height) != _pixelViewport)
            _pixelViewport = (width, height);

        if (_gl is { } viewport)
            viewport.Viewport(0, 0, (uint)width, (uint)height);

        _graphics?.Resize(width, height);

        // 尺寸只记第一行：它是「画面与射线共用同一把尺」这件事在现场的证据。
        if (!_viewportReported)
        {
            _viewportReported = true;
            (int layoutWidth, int layoutHeight) = LayoutPixelSize();
            Logger.Info($"Viewport: {width}x{height} px（{_viewportSource}）· " +
                        $"控件布局 {Bounds.Width:F0}x{Bounds.Height:F0} · 显示缩放 {RenderScaling:F2} " +
                        $"= {layoutWidth}x{layoutHeight} px · 每布局单位 {PixelsPerLayoutUnit.X:F4}x{PixelsPerLayoutUnit.Y:F4} px");
        }

        return (width, height);
    }

    /// <summary>
    /// 当前绑定的 framebuffer 颜色附着的尺寸，直接问 GL —— 那才是合成器随后采样、画面真正所在的像素网格。
    /// 附着既不是渲染缓冲也不是纹理时（默认 framebuffer）返回 <c>(0, 0)</c>，由调用方退回预测。
    /// </summary>
    private static (int Width, int Height) QueryFramebufferSize(GL gl)
    {
        gl.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            FramebufferAttachmentParameterName.ObjectType, out int attachmentType);

        if (attachmentType == (int)GLEnum.Renderbuffer)
        {
            gl.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferAttachmentParameterName.ObjectName, out int renderbuffer);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, (uint)renderbuffer);
            gl.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer,
                RenderbufferParameterName.Width, out int bufferWidth);
            gl.GetRenderbufferParameter(RenderbufferTarget.Renderbuffer,
                RenderbufferParameterName.Height, out int bufferHeight);
            gl.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
            return (bufferWidth, bufferHeight);
        }

        if (attachmentType == (int)GLEnum.Texture)
        {
            gl.GetFramebufferAttachmentParameter(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
                FramebufferAttachmentParameterName.ObjectName, out int texture);
            gl.BindTexture(TextureTarget.Texture2D, (uint)texture);
            gl.GetTexLevelParameter(TextureTarget.Texture2D, 0, GLEnum.TextureWidth, out int textureWidth);
            gl.GetTexLevelParameter(TextureTarget.Texture2D, 0, GLEnum.TextureHeight, out int textureHeight);
            gl.BindTexture(TextureTarget.Texture2D, 0);
            return (textureWidth, textureHeight);
        }

        return (0, 0);
    }

    /// <summary>已量到的像素矩形；首帧之前（或量不到时）退回按布局尺寸预测。</summary>
    private (int Width, int Height) PixelViewport =>
        _pixelViewport.Width > 0 ? _pixelViewport : LayoutPixelSize();

    /// <summary>本控件布局尺寸换算成物理像素 —— 只是首帧之前的预测，之后一切以量到的表面为准。</summary>
    private (int Width, int Height) LayoutPixelSize() => (
        Math.Max(1, (int)Math.Ceiling(Bounds.Width * RenderScaling)),
        Math.Max(1, (int)Math.Ceiling(Bounds.Height * RenderScaling)));

    /// <summary>
    /// 物理像素 / 布局单位：指针（永远在布局坐标里）要走多远才等于 framebuffer 上的一个像素。
    /// 取自【实际在用的矩形】而不是 <see cref="RenderScaling"/>，所以合成器在分数缩放上的取整
    /// 不可能把射线挪一两个像素 —— 那种误差随控件变大而变大，表现就是「点击滑到部件旁边」。
    /// </summary>
    private Vector2 PixelsPerLayoutUnit
    {
        get
        {
            (int width, int height) = PixelViewport;
            if (Bounds.Width <= 0 || Bounds.Height <= 0)
                return new Vector2((float)RenderScaling, (float)RenderScaling);

            return new Vector2(width / (float)Bounds.Width, height / (float)Bounds.Height);
        }
    }

    /// <summary>
    /// 本控件所在窗口的显示缩放（布局单位 → 物理像素的预测值）。
    /// 只用于首帧之前的预测：合成器给本控件的表面未必等于 <c>Bounds × 它</c>，拿它当真相就会偏。
    /// </summary>
    private double RenderScaling => (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;

    /// <summary>
    /// 单击必须保住的轨道机位：按下时抓取、投射射线前放回 —— 拾取只有对着用户点下去的那一帧才有意义。
    /// </summary>
    private readonly record struct CameraPose(float Yaw, float Pitch, float Distance, Vector3 Target)
    {
        public static CameraPose Capture(Camera camera)
            => new(camera.Yaw, camera.Pitch, camera.Distance, camera.Target);

        public void Restore(Camera camera)
        {
            camera.Target = Target;
            camera.Distance = Distance;
            camera.Yaw = Yaw;
            camera.Pitch = Pitch;
        }
    }

    /// <summary>两点之间的屏幕距离（单击 / 拖拽的判据）。</summary>
    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
