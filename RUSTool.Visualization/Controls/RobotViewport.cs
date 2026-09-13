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
/// <item><b>画到哪去</b>：每帧绑定控件自己的 framebuffer、按控件尺寸同步视口、渲染、请求下一帧。</item>
/// <item><b>指针怎么变成相机</b>：rviz 风格左键旋转 / 中键平移 / 右键缩放 / 单击拾取高亮。</item>
/// </list>
///
/// <para>
/// 数据入口只有一个 <see cref="JointValues"/>（纯 float 列表，单位弧度）——界面把 <c>/state</c>
/// 的关节角喂进来，模型就动。这个属性上不出现任何 RobotSimulation / Silk.NET 类型，
/// 因此 RUSTool.UI 可以在不引用图形栈的前提下绑定它，也仍然能被离屏截图模式渲染。
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

    // ── 渲染状态：只在 GL 回调里存在，随上下文一起生灭 ──
    private GL? _gl;
    private IRenderContext? _graphics;
    private IRenderer? _renderer;
    private RobotScene? _robotScene;
    private GameObject? _selected;

    // ── 关节帧邮箱：UI 线程只写这一格，渲染线程取走即置空（同一帧不会被重复应用）──
    private readonly object _jointsLock = new();
    private float[]? _pendingJoints;
    private bool _jointMismatchReported;

    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private TimeSpan _lastFrameTime;
    private double _statsAccumulator;

    // ── 指针状态：事件挂在窗口上（见 OnAttachedToVisualTree），这里只记过程 ──
    private bool _dragging;
    private bool _dragged;
    private Point _pressPosition;
    private Point _lastPosition;

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

        // 视口按【物理像素】跟随控件尺寸，否则 HiDPI 屏上画面发糊。
        double scaling = (VisualRoot as TopLevel)?.RenderScaling ?? 1.0;
        int pixelWidth = Math.Max(1, (int)(Bounds.Width * scaling));
        int pixelHeight = Math.Max(1, (int)(Bounds.Height * scaling));
        _graphics.Resize(pixelWidth, pixelHeight);

        SceneGraph scene = _robotScene.Graph;
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
        _selected = null;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        // GL 控件的内容会被合成器当作一张独立表面，命中测试直接穿到父级 ——
        // 控件自己收不到指针事件。所以监听窗口，再用本控件的矩形过滤。
        if (VisualRoot is InputElement root)
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
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (VisualRoot is InputElement root)
        {
            root.RemoveHandler(PointerPressedEvent, OnWindowPointerPressed);
            root.RemoveHandler(PointerMovedEvent, OnWindowPointerMoved);
            root.RemoveHandler(PointerReleasedEvent, OnWindowPointerReleased);
            root.RemoveHandler(PointerWheelChangedEvent, OnWindowPointerWheel);
        }

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
        _pressPosition = _lastPosition = e.GetPosition(this);
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging || _robotScene is null)
            return;

        Point position = e.GetPosition(this);
        if (Distance(position, _pressPosition) > ClickDragThresholdPixels)
            _dragged = true; // 明确的拖拽发生了 → 松手时不能再当单击拾取

        var delta = new Vector2((float)(position.X - _lastPosition.X), (float)(position.Y - _lastPosition.Y));
        _lastPosition = position;

        PointerPoint point = e.GetCurrentPoint(this);
        if (point.Properties.IsLeftButtonPressed)
            // 屏幕 Y 向下、相机俯仰向上：不取反的话拖拽方向是反的。
            _robotScene.Graph.Camera.Rotate(-delta.X * RotateDegreesPerPixel, -delta.Y * RotateDegreesPerPixel);
        else if (point.Properties.IsMiddleButtonPressed)
            _robotScene.Graph.Camera.Pan(delta * PanScale);
        else if (point.Properties.IsRightButtonPressed)
            _robotScene.Graph.Camera.Zoom(-delta.Y * ZoomPerDragPixel); // 向上拖（dy<0）＝拉近

        RequestNextFrameRendering();
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_dragging)
            return;

        _dragging = false;

        // 单击选中、拖拽只转相机。
        if (!_dragged && _robotScene is not null && IsPointerInside(e))
            PickAt(e.GetPosition(this));
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
    /// 屏幕像素 → 世界射线 → 拾取并单选高亮。相机是纯 CPU 数据，这条射线投射完全不碰 GL。
    /// </summary>
    private void PickAt(Point position)
    {
        SceneGraph scene = _robotScene!.Graph;
        Ray ray = scene.Camera.ScreenToWorldRay(
            new Vector2((float)position.X, (float)position.Y),
            new Vector2((float)Bounds.Width, (float)Bounds.Height));

        GameObject? picked = scene.PickAndHighlight(ray, enable: true);

        // 单选：新的命中替换旧的，落空则清空。
        if (_selected is { } previous && !ReferenceEquals(previous, picked))
            previous.Highlighted = false;
        if (picked is null && _selected is not null)
            _selected.Highlighted = false;
        _selected = picked;

        string message = picked is null ? "单击拾取：未命中" : $"单击拾取：{picked.Name}";
        Dispatcher.UIThread.Post(() => Picked?.Invoke(message));
        RequestNextFrameRendering();
    }

    /// <summary>两点之间的屏幕距离（单击 / 拖拽的判据）。</summary>
    private static double Distance(Point a, Point b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
