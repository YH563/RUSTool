using Avalonia;
using Avalonia.Controls;
using RUSTool.Services.Logging;
using RUSTool.UI.ViewModels;
using RUSTool.Visualization.Controls;
using System;

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
    /// 把它的实测高度填进来，右下角的朝向 gizmo 会自动收在这条线下方；默认 0 = 库的默认尺寸。
    /// 只做一件事 —— 转发给 <see cref="RobotViewport.GizmoTopInset"/>。
    ///
    /// <para>
    /// 为什么要绕一道：浮动层是**工作区**那一层摆的（卡片内部只有视口，看不到它），
    /// 而视口正好是知道 gizmo 算法的那一层。工作区把浮动层的实测高度绑到这个属性上即可——
    /// 卡片内部不必知道上面压的是什么，界面也不必知道 gizmo 怎么算。
    /// </para>
    /// <para>
    /// 两个工作区各取一种：工程师工作区**没有**覆盖层 —— 机械臂状态栏停靠在视口右侧的独立一列
    /// （见 <c>DebugWorkspace.axaml</c>），视口本身不该被压住，所以那里不设这个属性；
    /// 临床工作区右上角还是一条「末端接触力」小浮层，仍然靠它给 gizmo 让位。
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

    /// <summary>数据上下文里的主 VM（订阅菜单事件用）。</summary>
    private MainViewModel? _wiredViewModel;

    /// <summary>
    /// 菜单「重置视角」→ VM 的事件 → 这里让相机复位。
    /// 订阅放在挂树时、退订放在摘树时：VM 比视图活得久（还可能被别的窗口复用），
    /// 不退订就会把视图钉在内存里。
    ///
    /// <para>
    /// 挂树同时也是取日志服务的时机：它来自数据上下文，而 GL 初始化晚于挂树，
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
            _log = viewModel.LogService;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_wiredViewModel is not null)
        {
            _wiredViewModel.ViewResetRequested -= OnViewResetRequested;
            _wiredViewModel = null;
        }

        _log = null;
        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>相机复位在无 GL 时是静默空操作（视口自己判断），所以这里不需要额外保护。</summary>
    private void OnViewResetRequested() => Viewport.ResetCamera();
}
