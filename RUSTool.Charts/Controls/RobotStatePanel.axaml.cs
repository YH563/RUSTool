using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using LiveChartsCore.SkiaSharpView.Avalonia;
using RUSTool.Charts.Data;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace RUSTool.Charts.Controls;

/// <summary>
/// 「数据曲线」组合控件：若干行上下排开，一行一路实时曲线（<see cref="RobotStateRow"/>）；
/// 行头是「色标 + 通道名 + 最新读数」；一行数据都没有时只留一句「为什么没有」。
///
/// <para>
/// <b>高度是等分的、不是固定的：</b>六行装在等分格子里（<c>UniformGrid</c>），
/// 宿主给多高就平分多高，行间留一道底色缝 —— 卡片体变大时曲线跟着长高，不会「下面留一片白」。
/// 代价是宿主必须给出<b>确定的高度</b>（例如卡片体那一行是 <c>*</c>）：塞进无限高的容器
/// （滚动列表 / <c>Auto</c> 行）时，等分就退化成按内容量高，六行会各自缩成一条。
/// </para>
///
/// <para>
/// <b>怎么用（宿主侧只需要三步）：</b>
/// <code>
/// var rows = RobotStateRows.Create(RobotArmChannels.Torques());   // 1. 建行（也可以由 ViewModel 持有）
/// panel.Rows = rows;                                             // 2. 交给控件（或 XAML 里绑定 Rows）
/// panel.PushFrame(state.Effort);                                 // 3. 每来一帧推一次（必须在 UI 线程）
/// </code>
/// </para>
/// <para>
/// <b>行是「外部拥有」的：</b>控件<b>不</b>造行、也不换行 —— 它只渲染你给的集合、并往行里推帧。
/// 这样 ViewModel 拿着同一个集合就能算出「窗口还有多久满」之类的信息，
/// 而控件也不需要自己再存一份「有几行」的状态。
/// </para>
/// <para>
/// <b><see cref="HasData"/> 是算出来的、不是设进来的：</b>控件监听着每行点集合的变更，
/// 有任何一个点就显示曲线区、一个点都没有就显示提示语。调用方永远不用记得「推完帧还要把 HasData 置真」——
/// 忘一次就变成「曲线在跳、提示语还挂在中间」。
/// </para>
/// <para>
/// <b>依赖关系：</b>本控件只依赖主题<b>令牌</b>（<c>SurfaceSunkenBrush</c> / <c>BorderSubtleBrush</c> /
/// <c>TextPrimaryBrush</c> / <c>TextTertiaryBrush</c> / <c>FontMono</c> / <c>FontSizeBody</c> /
/// <c>FontSizeCaption</c>），不要求宿主提供任何布局类；令牌缺失时取默认值，不会抛异常。
/// 图表库（LiveCharts + SkiaSharp）完全封在本控件内部，宿主的 XAML 里看不到它们。
/// </para>
/// </summary>
public partial class RobotStatePanel : UserControl
{
    /// <summary>要画的行（外部拥有；<c>null</c> 或空集合就是「什么都还没得画」）。</summary>
    public static readonly StyledProperty<ObservableCollection<RobotStateRow>?> RowsProperty =
        AvaloniaProperty.Register<RobotStatePanel, ObservableCollection<RobotStateRow>?>(nameof(Rows));

    /// <summary>控制通道是否在线。只用来决定空数据提示的措辞（见 <see cref="HintText"/>）。</summary>
    public static readonly StyledProperty<bool> IsConnectedProperty =
        AvaloniaProperty.Register<RobotStatePanel, bool>(nameof(IsConnected));

    /// <summary>
    /// 空数据提示文案；<c>null</c>（默认）= 按 <see cref="IsConnected"/> 自动选一句。
    /// 留这个口子是因为「没连上」和「连上了但还没来帧」这两种措辞很依赖上下文，
    /// 宿主想说的话可能比默认那两句更具体。
    /// </summary>
    public static readonly StyledProperty<string?> HintTextProperty =
        AvaloniaProperty.Register<RobotStatePanel, string?>(nameof(HintText));

    /// <summary>默认提示（还没连上控制通道）：查的方向是连接，不是数据。</summary>
    private const string HintDisconnectedText = "未连接控制通道 · 曲线待数据";

    /// <summary>默认提示（连上了但还没来帧）：查的方向是后端 / 状态流有没有开。</summary>
    private const string HintWaitingText = "等待状态帧…";

    /// <summary>当前正在监听点集合的那些行（换集合或换行时按这份名单退订）。</summary>
    private readonly List<RobotStateRow> _watched = [];

    /// <summary>当前订阅了 <c>CollectionChanged</c> 的那个集合（换 Rows 时要退订旧的）。</summary>
    private ObservableCollection<RobotStateRow>? _subscribed;

    public RobotStatePanel()
    {
        InitializeComponent();
        UpdateEmptyState();
    }

    /// <summary>要画的行。</summary>
    public ObservableCollection<RobotStateRow>? Rows
    {
        get => GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    /// <summary>控制通道是否在线。</summary>
    public bool IsConnected
    {
        get => GetValue(IsConnectedProperty);
        set => SetValue(IsConnectedProperty, value);
    }

    /// <summary>空数据提示文案；<c>null</c> = 自动。</summary>
    public string? HintText
    {
        get => GetValue(HintTextProperty);
        set => SetValue(HintTextProperty, value);
    }

    /// <summary>
    /// 是否已经有数据（= 任意一行有一个点）。<b>只读派生值</b>：由点集合的变更自动翻，
    /// 不给外部设 —— 理由见类注释。做成只读属性（不是普通 CLR 属性）是为了它能被绑定。
    /// </summary>
    private static readonly DirectProperty<RobotStatePanel, bool> HasDataProperty =
        AvaloniaProperty.RegisterDirect<RobotStatePanel, bool>(nameof(HasData), panel => panel.HasData);

    /// <summary>当前是否已经有数据（<see cref="HasData"/> 的后备字段）。</summary>
    private bool _hasData;

    /// <summary>当前是否已经有数据（见 <see cref="HasDataProperty"/>）。</summary>
    public bool HasData
    {
        get => _hasData;
        private set => SetAndRaise(HasDataProperty, ref _hasData, value);
    }

    /// <summary>
    /// 推一帧：交给每一行，各行取自己那一路的分量。
    /// <b>必须在 UI 线程调用</b>（改的是绑定源集合）。
    /// </summary>
    /// <param name="frame">
    /// 一帧数据，下标与通道目录对齐（例如状态帧的 <c>effort</c>）；
    /// 分量不够时缺的那些按 0 计（帧字段在协议演进中可能先到一半）。
    /// </param>
    public void PushFrame(IReadOnlyList<double> frame) => RobotStateRows.PushFrame(Rows, frame);

    /// <summary>
    /// 清空所有行的历史与读数（曲线回到「一条都没有」，提示语回来）。
    /// 例如重新连接后端时用：旧的形状不该还挂在那里冒充新数据。
    /// <b>必须在 UI 线程调用</b>。
    /// </summary>
    public void ClearHistory() => RobotStateRows.Clear(Rows);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == RowsProperty)
            AttachRows(change.GetNewValue<ObservableCollection<RobotStateRow>?>());
        else if (change.Property == IsConnectedProperty || change.Property == HintTextProperty)
            UpdateEmptyState();
    }

    /// <summary>
    /// 换一组行：退订旧的、订阅新的（行集合本身 + 每行的点集合）。
    /// 订阅点集合是为了让 <see cref="HasData"/> 自己翻 —— 调用方永远不用记得同步它。
    /// </summary>
    private void AttachRows(ObservableCollection<RobotStateRow>? rows)
    {
        if (_subscribed is not null)
            _subscribed.CollectionChanged -= OnRowsCollectionChanged;

        UnwatchRows();

        _subscribed = rows;
        RowList.ItemsSource = rows;

        if (rows is not null)
        {
            rows.CollectionChanged += OnRowsCollectionChanged;
            WatchRows();
        }

        UpdateEmptyState();
    }

    /// <summary>行集合本身变了（运行中增删行 / 整体替换）：按当前内容重新挂钩，再刷新空状态。</summary>
    private void OnRowsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UnwatchRows();
        WatchRows();
        UpdateEmptyState();
    }

    /// <summary>挂上每一行点集合的变更通知（名单留着，退订时按它走）。</summary>
    private void WatchRows()
    {
        if (Rows is not { } rows)
            return;

        foreach (var row in rows)
        {
            row.Values.CollectionChanged += OnValuesChanged;
            _watched.Add(row);
        }
    }

    /// <summary>退掉所有点集合的变更通知。</summary>
    private void UnwatchRows()
    {
        foreach (var row in _watched)
            row.Values.CollectionChanged -= OnValuesChanged;

        _watched.Clear();
    }

    /// <summary>
    /// 某一行有点进 / 出：空状态可能变了（第一帧到 / 被清空）。
    /// 每帧都会走到这里（推一帧 = 每行各一次「加尾 + 删头」），所以 <see cref="UpdateEmptyState"/>
    /// 只做常数级的事、并且结果没变就不通知。
    /// </summary>
    private void OnValuesChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateEmptyState();

    /// <summary>
    /// 按「有没有数据」切换曲线区与提示语，并把提示语定下来（自动文案见 <see cref="HintText"/>）。
    /// </summary>
    private void UpdateEmptyState()
    {
        HasData = Rows is not null && Rows.Any(row => row.Values.Count > 0);

        ChartArea.IsVisible = HasData;
        HintLabel.IsVisible = !HasData;
        HintLabel.Text = HintText ?? (IsConnected ? HintWaitingText : HintDisconnectedText);
    }

    /// <summary>
    /// 让树里每张曲线图立刻按最新数据重画一次，返回重画的张数 —— 只给截图模式用
    /// （见 <c>Program.cs</c> 的 <c>--demo-torque</c>）。
    ///
    /// <para>
    /// 正常运行时不需要：图表更新有自己的节流器（把一帧内的多次变更合批再画）。
    /// 而截图进程是「推完数据马上就要拍」，等不到那个计时器 —— 不等的话 PNG 里拍到的是一圈空坐标轴。
    /// </para>
    /// <para>
    /// 做成<b>静态</b>方法（收一个可视树根、而不是在面板实例上找自己的图）：截图模式下窗口刚布局完，
    /// 面板实例还不好拿，而「树里所有曲线图」这个集合是可数的、也正好是这里要的。<b>必须在 UI 线程调用</b>。
    /// </para>
    /// </summary>
    public static int RedrawAll(Visual root)
    {
        var count = 0;
        foreach (var chart in root.GetVisualDescendants().OfType<CartesianChart>())
        {
            // 注意：图上那个 UpdaterThrottler 属性是「攒批间隔」（TimeSpan），不是能立刻催账的节流器；
            // 「马上按当前数据重算重画」的入口在核心图表上 —— 把 Throttling 关掉即可。
            // （类型在 LiveChartsCore.Kernel：只为这一处不 import 整个核心命名空间。）
            chart.CoreChart.Update(new LiveChartsCore.Kernel.ChartUpdateParams { Throttling = false });
            count++;
        }

        return count;
    }
}
