using Avalonia.Controls;
using RUSTool.UI.ViewModels;
using RUSTool.UI.Views.Clinical;
using RUSTool.UI.Views.Debug;
using System;
using System.ComponentModel;

namespace RUSTool.UI.Views;

/// <summary>
/// 主窗口。窗口自己只做一件事：<b>换工作区</b> —— 把当前模式的那一份挂到内容区、
/// 把另一份摘下来；其余行为仍然全部由绑定表达。
///
/// <para>
/// 为什么这件事不能写成 XAML 里的两个 <c>IsVisible</c>：两个工作区里各有一个 3D 视口
/// （<c>Scene3DView</c> 里的 <c>OpenGlControlBase</c>），而 GL 资源是跟着**挂树期**生灭的 ——
/// <c>IsVisible=false</c> 只是不参与合成，控件的 GL 上下文、场景图、每帧重绘的循环都还在。
/// 两个都挂在树上时，切换之后就有两套 GL 上下文、两份 URDF/STL 模型、两条渲染循环同时活着，
/// 画面会在两个视口的状态之间交替 —— 表现就是坐标系与末端法兰持续频闪（本机实测）。
/// 摘下来的一方在 <c>OnOpenGlDeinit</c> 里释放 GL，于是任何时刻只有一个视口在画。
/// </para>
/// <para>
/// 两份工作区是**缓存复用**的（不是每次切换 new 一个）：与 GL 无关的界面状态
/// （滚动位置、日志面板、状态浮层展开与否）在来回切换时保留下来，
/// 重建的只有图形栈那一层；代价是每次切换重新初始化一次图形栈（日志里会多一行 <c>[3d] 就绪</c>）。
/// </para>
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>工程师工作区（3D + 影像 + 曲线 + 控制台）。</summary>
    private readonly DebugWorkspace _debug = new();

    /// <summary>临床工作区（扫查视图 + 线性流程 + 急停）。</summary>
    private readonly ClinicalWorkspace _clinical = new();

    /// <summary>已订阅的 VM；换数据上下文时退订，不把窗口钉在旧 VM 上。</summary>
    private MainViewModel? _wiredViewModel;

    public MainWindow()
    {
        InitializeComponent();

        // 数据上下文可能早于也可能晚于窗口显示（真实运行在 App 里给、截图模式在 Program 里给），
        // 两种时序都走同一条「取到 VM → 挂当前模式的工作区」，所以挂在事件上而不是构造函数里做一次。
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_wiredViewModel is not null)
        {
            _wiredViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _wiredViewModel = null;
        }

        if (DataContext is MainViewModel viewModel)
        {
            _wiredViewModel = viewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        ApplyWorkspace();
    }

    /// <summary>只认模式位：其余属性一秒一变，不该让窗口跟着做事。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsDebugMode))
            ApplyWorkspace();
    }

    /// <summary>
    /// 把当前模式的那一份挂进内容区。
    ///
    /// <para>
    /// 赋同一个实例要跳过：Avalonia 对同一个 <c>Content</c> 重复赋值仍会走一遍摘除 / 挂载，
    /// 等于白重建一次图形栈。数据上下文还没到位（或不是主 VM）时按工程师模式挂 ——
    /// 与 <see cref="MainViewModel.IsDebugMode"/> 的默认值一致。
    /// </para>
    /// </summary>
    private void ApplyWorkspace()
    {
        Control workspace = _wiredViewModel is { IsDebugMode: false } ? _clinical : _debug;

        if (!ReferenceEquals(WorkspaceHost.Content, workspace))
            WorkspaceHost.Content = workspace;
    }
}
