using Avalonia;
using Avalonia.Controls;
using RUSTool.Communication;
using RUSTool.Services.Logging;
using RUSTool.UI.ViewModels;
using RUSTool.Visualization.Controls;
using RUSTool.Visualization.Scene;
using System;
using System.Threading;

namespace RUSTool.UI.Views.Debug;

/// <summary>
/// 3D 场景窗 —— 界面层与图形栈之间唯一的接缝。
///
/// <para>
/// 视图逻辑只有一件事：把 <see cref="RobotViewport"/> 抛出来的诊断信息落到界面上。
/// GPU / 模型加载报告与 FPS 走左下角角标（截图里也看得见），初始化失败则隐藏视口、
/// 把原因写进保底层的占位文字 —— 界面上永远不出现"什么都没有"的黑块。
/// </para>
/// <para>
/// 诊断同时写 stderr，理由与 <c>Program.cs</c> 的截图诊断一致：stderr 无缓冲，
/// 进程被 kill（窗口模式就是靠 kill 结束的）也不会丢掉最后一段；而且一条 <c>[3d]</c>
/// 开头的行可以被脚本直接 grep，不需要人去盯屏幕。
/// </para>
/// <para>
/// 就绪 / 失败还额外写进全局日志（<see cref="ILogService"/>）：GL 初始化的失败原因发生在
/// Avalonia + 驱动这一侧，库自己的日志覆盖不到，只有进了日志面板与落盘文件才留得下来。
/// 每秒一行的性能行不进日志 —— 它只上角标，否则会把面板刷满（限长 500 条）。
/// </para>
/// </summary>
public partial class Scene3DView : UserControl
{
    /// <summary>日志来源列文本（落盘文件里看得到：<c>[3d] …</c>）。</summary>
    private const string LogSource = "3d";

    /// <summary>全局日志服务；由数据上下文（<see cref="MainViewModel.LogService"/>）在挂树时取到。</summary>
    private ILogService? _log;

    public Scene3DView()
    {
        InitializeComponent();

        // GL 就绪：一次性报告（GPU / 版本 / 加载了哪个模型）。
        Viewport.Ready += report =>
        {
            Console.Error.WriteLine($"[3d] 就绪 {report}");
            ViewportStatus.Text = report;
            _log?.Log($"图形栈就绪：{report}", LogLevel.Info, LogSource);
        };

        // 每秒一行性能信息：只更新角标，不再往 stderr 刷屏（就绪那行已经足够证明 GL 通着），
        // 也不进日志（每秒一条会把面板里真正有用的东西挤掉）。
        Viewport.Stats += line => ViewportStatus.Text = line;

        // 单击拾取的结果（相机操作是纯 CPU，无 GL 也能用，所以单独一行提示）。
        Viewport.Picked += message =>
        {
            Console.Error.WriteLine($"[3d] {message}");
            _log?.Log(message, LogLevel.Debug, LogSource);
        };

        // 初始化失败：不画、不崩、不留黑块 —— 隐藏视口让占位层露出来，并把原因写给人看。
        Viewport.Failed += reason =>
        {
            Console.Error.WriteLine($"[3d] 初始化失败：{reason}");
            Viewport.IsVisible = false;
            FallbackText.Text = $"3D 视图不可用：{reason}";
            _log?.Log($"3D 视图不可用：{reason}", LogLevel.Error, LogSource);
        };
    }

    /// <summary>
    /// 视口顶部被覆盖层占掉的高度（逻辑像素）—— 界面若在视口上叠了浮动层（状态 HUD / 工具条），
    /// 把它的实测高度填进来：覆盖层下面放不下库的最小 gizmo 方块时，右下角的朝向 gizmo 就整块不画。
    /// 0.3.0 起 gizmo 尺寸归渲染器（视口短边 × 0.12，夹在 64~240 px），视口能决定的只剩
    /// 「让不让位」而不是「缩到多小」，所以这个值不再影响 gizmo 的大小，只影响它出不出来；
    /// 默认 0 = 不设覆盖层。只做一件事 —— 转发给 <see cref="RobotViewport.GizmoTopInset"/>。
    ///
    /// <para>
    /// 为什么要绕一道：浮动层是**工作区**那一层摆的（卡片内部只有视口，看不到它），
    /// 而视口正好是知道 gizmo 算法的那一层。工作区把浮动层的实测高度绑到这个属性上即可——
    /// 卡片内部不必知道上面压的是什么，界面也不必知道 gizmo 怎么算。
    /// </para>
    /// <para>
    /// 两个工作区各取一种：工程师工作区**不设**它 —— 那里的状态读数收成右上角一枚按需展开的小浮层
    /// （见 <c>DebugWorkspace.axaml</c>），而 gizmo 在右下角，两者不打架，视口本身默认是完整的；
    /// 临床工作区右上角是一条常驻的「末端接触力」小浮层，仍然靠它决定 gizmo 让不让位。
    /// </para>
    /// </summary>
    public static readonly StyledProperty<double> GizmoTopInsetProperty =
        AvaloniaProperty.Register<Scene3DView, double>(nameof(GizmoTopInset));

    /// <inheritdoc cref="GizmoTopInsetProperty"/>
    public double GizmoTopInset
    {
        get => GetValue(GizmoTopInsetProperty);
        set => SetValue(GizmoTopInsetProperty, value);
    }

    /// <summary>
    /// 转发 <see cref="GizmoTopInsetProperty"/> 到视口。
    /// XAML 里的属性赋值发生在构造函数之后，所以这时 <c>Viewport</c> 一定已经建好（无需判空）。
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == GizmoTopInsetProperty)
            Viewport.GizmoTopInset = change.NewValue is double inset ? inset : 0d;
    }

    /// <summary>数据上下文里的主 VM（订阅菜单事件与点云流用）。</summary>
    private MainViewModel? _wiredViewModel;

    /// <summary>首帧点云只报一次（WS 线程可能每 100ms 就来一帧，不能每帧写日志）。</summary>
    private int _pointCloudReported;

    /// <summary>
    /// 菜单「重置视角」→ VM 的事件 → 这里让相机复位。
    /// 订阅放在挂树时、退订放在摘树时：VM 比视图活得久（还可能被别的窗口复用），
    /// 不退订就会把视图钉在内存里。
    ///
    /// <para>
    /// 挂树同时也是取日志服务与订阅点云流的时机：它来自数据上下文，而 GL 初始化晚于挂树，
    /// 所以视口抛事件时 <c>_log</c> 一定已经就位。
    /// </para>
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is MainViewModel viewModel)
        {
            _wiredViewModel = viewModel;
            viewModel.ViewResetRequested += OnViewResetRequested;
            viewModel.PointCloudFrameReceived += OnPointCloudFrame;
            _log = viewModel.LogService;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_wiredViewModel is not null)
        {
            _wiredViewModel.ViewResetRequested -= OnViewResetRequested;
            _wiredViewModel.PointCloudFrameReceived -= OnPointCloudFrame;
            _wiredViewModel = null;
        }

        _log = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>相机复位在无 GL 时是静默空操作（视口自己判断），所以这里不需要额外保护。</summary>
    private void OnViewResetRequested() => Viewport.ResetCamera();

    /// <summary>
    /// 感知点云帧到达（<b>WS 线程</b>）→ 换成视口认识的形状 → 丢进视口的邮箱。
    ///
    /// <para>
    /// 这一层只做两件事：把 Core 的 <see cref="SensorPointCloudFrame"/> 适配成
    /// <see cref="PointCloudFrame"/>（两个工程互不认识，适配点只能在这里），
    /// 以及投递。数组按所有权交接、不复制 —— 帧本来就是覆盖式的，
    /// 视口只保留到渲染线程取走为止（见 <see cref="RobotViewport.SubmitPointCloud"/>）。
    /// </para>
    /// <para>
    /// 首帧写一条日志（此后不写）：点云端到端接通的第一手证据就在日志面板与落盘文件里，
    /// 与 <c>[3d]</c> 那几行（就绪 / 拾取 / 失败）一样可以事后查。
    /// </para>
    /// </summary>
    private void OnPointCloudFrame(SensorPointCloudFrame frame)
    {
        Viewport.SubmitPointCloud(new PointCloudFrame(
            frame.Xyz, frame.Rgb, frame.Count, frame.Seq, frame.Scope, frame.Timestamp));

        if (Interlocked.Exchange(ref _pointCloudReported, 1) == 0)
        {
            string line = $"点云流已接通：首帧 {frame.Count} 点（seq {frame.Seq} · " +
                          $"{(frame.Scope.Length > 0 ? frame.Scope : "未知 scope")} · {frame.Encoding}）";
            Console.Error.WriteLine($"[3d] {line}");
            _log?.Log(line, LogLevel.Info, LogSource);
        }
    }
}
