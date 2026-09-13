using Avalonia;
using Avalonia.Controls;
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
/// </summary>
public partial class Scene3DView : UserControl
{
    public Scene3DView()
    {
        InitializeComponent();

        // GL 就绪：一次性报告（GPU / 版本 / 加载了哪个模型）。
        Viewport.Ready += report =>
        {
            Console.Error.WriteLine($"[3d] 就绪 {report}");
            ViewportStatus.Text = report;
        };

        // 每秒一行性能信息：只更新角标，不再往 stderr 刷屏（就绪那行已经足够证明 GL 通着）。
        Viewport.Stats += line => ViewportStatus.Text = line;

        // 单击拾取的结果（相机操作是纯 CPU，无 GL 也能用，所以单独一行提示）。
        Viewport.Picked += message => Console.Error.WriteLine($"[3d] {message}");

        // 初始化失败：不画、不崩、不留黑块 —— 隐藏视口让占位层露出来，并把原因写给人看。
        Viewport.Failed += reason =>
        {
            Console.Error.WriteLine($"[3d] 初始化失败：{reason}");
            Viewport.IsVisible = false;
            FallbackText.Text = $"3D 视图不可用：{reason}";
        };
    }

    /// <summary>数据上下文里的主 VM（订阅菜单事件用）。</summary>
    private MainViewModel? _wiredViewModel;

    /// <summary>
    /// 菜单「重置视角」→ VM 的事件 → 这里让相机复位。
    /// 订阅放在挂树时、退订放在摘树时：VM 比视图活得久（还可能被别的窗口复用），
    /// 不退订就会把视图钉在内存里。
    /// </summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        if (DataContext is MainViewModel viewModel)
        {
            _wiredViewModel = viewModel;
            viewModel.ViewResetRequested += OnViewResetRequested;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_wiredViewModel is not null)
        {
            _wiredViewModel.ViewResetRequested -= OnViewResetRequested;
            _wiredViewModel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    /// <summary>相机复位在无 GL 时是静默空操作（视口自己判断），所以这里不需要额外保护。</summary>
    private void OnViewResetRequested() => Viewport.ResetCamera();
}
