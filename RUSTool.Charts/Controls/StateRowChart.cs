using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Avalonia;
using LiveChartsCore.SkiaSharpView.Painting;
using RUSTool.Charts.Data;
using SkiaSharp;
using System;

namespace RUSTool.Charts.Controls;

/// <summary>
/// 一行曲线（可复用控件）：把 <see cref="Source"/> 那一行的点画成一条线。
///
/// <para>
/// <b>图表为什么在代码里建、而不是写在 XAML 里：</b>
/// 图表要喂三样东西 —— 曲线的数据与配色、坐标轴、以及「主题一换就要重取一遍」。
/// 这三件事都是「取 Avalonia 资源 → 换成 Skia 的颜色」，而图表是画在 Skia 画布上的：
/// XAML 里的 <c>DynamicResource</c> 到不了它那里。所以本控件的 XAML 只有外壳（见
/// <c>RobotStatePanel.axaml</c>），图表由这里建。
/// </para>
/// <para>
/// <b>数据不在这里：</b>点由 <see cref="RobotStateRow.Values"/> 提供，这里只是把那个集合接给图表。
/// 滚动、清空重填都是行模型的事，控件不插手中途的点。
/// </para>
/// <para>
/// <b>只有 <see cref="Source"/> 一个输入：</b>画哪一路（<see cref="RobotStateRow.ChannelIndex"/> ——
/// 同时也是线色）、窗口多长（<see cref="RobotStateRow.WindowFrames"/>）全在行里读 ——
/// 控件不复制一份状态，也就没有「两处状态谁说了算」的问题。行是「一路一行」的，
/// 所以这个输入从挂上到摘掉都不会变，线色不需要跟着谁改。
/// </para>
/// </summary>
public sealed class StateRowChart : ContentControl
{
    /// <summary>要画的那一行（<c>null</c> 时什么都不画）。</summary>
    public static readonly StyledProperty<RobotStateRow?> SourceProperty =
        AvaloniaProperty.Register<StateRowChart, RobotStateRow?>(nameof(Source));

    /// <summary>当前画出来的图（没建出来时为 <c>null</c>）。</summary>
    private CartesianChart? _chart;

    /// <summary>曲线本身。笔画颜色在建图时按通道定下（颜色标识「这是哪一路数据」）。</summary>
    private LineSeries<double>? _series;

    /// <summary>控件当前是否挂在可视树上 —— 建图与重取主题色都以此为前提（见 <see cref="Build"/>）。</summary>
    private bool _inTree;

    /// <summary>零线的不透明度：够看清「零在哪」，又不至于在六行里连成一组醒目横条。</summary>
    private const byte ZeroLineAlpha = 0x3D;

    public StateRowChart()
    {
        // 轴线（那条零线）也画在 Skia 画布上，不跟 DynamicResource 走 ——
        // 换主题时得由这里重新取一遍主题色，否则深色主题下会留着浅色主题的轴线。
        // 订阅的是自己实例上的事件（不是 Application 上的静态事件）：控件销毁时一起走，不会把谁留在内存里。
        ActualThemeVariantChanged += (_, _) => ApplyAxisTheme();
    }

    /// <summary>要画的那一行。</summary>
    public RobotStateRow? Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == SourceProperty)
            Rebuild();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _inTree = true;

        // 建图放在挂树之后：主题色这时才解析得到。
        if (_chart is null)
            Build();
        else
            ApplyAxisTheme();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _inTree = false;

        // 不销毁已建的图：卸载再挂载（例如切到临床模式又切回来）时留着它，
        // 否则每次切工作区都会重建六张图、把已有的曲线清空一次。重新挂上时重取一遍主题色就够了。
    }

    /// <summary>换一行：先扔掉旧的画面，再按新行重建（线色与窗口长度都跟着新行走）。</summary>
    private void Rebuild()
    {
        Detach();
        Build();
    }

    /// <summary>
    /// 建这一行的图。X 轴（帧序号）固定成整段窗口，所以曲线是从左往右长出来、长满之后开始往左滚 ——
    /// 而不是每次都缩放成「刚好填满」，那样就看不出「窗口还有多久满」。
    /// </summary>
    private void Build()
    {
        if (!_inTree || Source is not { } row)
            return;

        _series = new LineSeries<double>
        {
            // 集合实例归行模型所有，而且它从不整体替换（滚动更新是就地删头加尾）。别在这里复制或
            // 换成新集合 —— 换掉等于让图表丢了监听（唯一该换的时候是换 Source，那时整张图重建）。
            Values = row.Values,
            // 线色取通道下标：一路一个色（见 StatePalette），与行头那条色标同表同色。
            Stroke = new SolidColorPaint(StatePalette.For(row.ChannelIndex), StatePalette.StrokeWidth),
            // 一行只有 ~60px 高：线下面再垫一层渐变填充会把曲线本身糊掉。
            Fill = null,
            // 每帧落一个新点，点上再画标记就是一串珠子而不是曲线了。
            GeometrySize = 0,
            // 不做平滑：力矩是逐帧采样出来的信号，平滑出来的弧会让人以为真到过那个值。
            LineSmoothness = 0,
        };

        _chart = new CartesianChart
        {
            Series = new ISeries[] { _series },
            XAxes =
            [
                new Axis
                {
                    // 横轴是帧序号，对看曲线形状的人没有意义；一行才几十像素高，
                    // 省下这行数字比标出这条轴的两个端点更值（一屏是多长时间写在卡片头，
                    // 见 TorqueChartViewModel.WindowCaption）。
                    IsVisible = false,
                    // 只 IsVisible = false 不够：这一版 LiveCharts 仍然会为「看不见的」轴量出一条标签带，
                    // 把绘图区压扁（曲线被压成一条细缝，而字又塞不进去，于是连刻度都不画 —— 空间白占了）。
                    // 不打算画标签就别提供画笔，量标签这一步才会整个跳过（实测绘图区恢复成满高）。
                    LabelsPaint = null,
                    MinLimit = 0,
                    // 窗口长度从行里读（不是全局常量）：同一个面板上的两行可以有不同长度的窗口。
                    MaxLimit = row.WindowFrames - 1,
                },
            ],
            // 实时曲线要「看得见当下的形状」：缓动等于让画面永远落后数据半拍。
            // 顺带一个好处 —— 截图模式拿到的必然是最终形状，不会拍到动画中途。
            AnimationsSpeed = TimeSpan.Zero,
            // 图例和悬停气泡都不要：一行几十像素高，气泡盖住的正是曲线本身，
            // 而且气泡是图表库自带的浅色浮层，深浅主题都对不上。
            LegendPosition = LegendPosition.Hidden,
            TooltipPosition = TooltipPosition.Hidden,
        };

        Content = _chart;

        // 新图还没配 Y 轴与零线（两处的颜色都要按当前主题取）—— 建完立刻刷一次。
        ApplyAxisTheme();
    }

    /// <summary>扔掉当前的画面内容（只是不再引用，不是销毁给图表库的对象）。</summary>
    private void Detach()
    {
        _series = null;
        _chart = null;
        Content = null;
    }

    /// <summary>
    /// 按当前主题重刷 Y 轴（零线；刻度字不画，理由见方法内注释）。
    /// 轴的颜色来自主题资源，而主题资源在 Skia 画布上「取不到」—— 换主题时由这里再取一遍。
    /// </summary>
    private void ApplyAxisTheme()
    {
        if (_chart is null)
            return;

        // 只取一次颜色，但 new 一份画笔：画笔是可变的绘制对象，不跨图共享就没有「谁改了谁」的疑问。
        var labelColor = ThemeColor("TextTertiaryBrush", new SKColor(0x86, 0x8C, 0x9A));

        _chart.YAxes =
        [
            new Axis
            {
                // 不画 Y 刻度字：一行满打满算 ~50px 高，而「小量程」的行（范围才 1 N·m 的那种）
                // 会被自动缩放切成四五档，9px 的字挤进二三十像素里，叠成一坨糊字 ——
                // 有刻度反而比没刻度更难读。（试过把 LabelsDensity 调大 1.0 / 1.4 / 2.0：
                //  这一版库在这么矮的图上会干脆一个刻度都不画，所以这里明说「不标刻度」。）
                // 顺带一个好处：不提供画笔，库里量标签这一步整个跳过，绘图区拿到满宽满高
                //（与 X 轴同一个道理，见 Build）。
                LabelsPaint = null,
                // 但要一条零线：不依赖刻度字也能读的参考就剩它了 ——
                // 正负号、有没有压过中轴、摆幅围着谁摆，一眼看得出来。
                // 用刻度字的颜色压淡：够看清，又不至于在六行里连成一组醒目横条。
                ZeroPaint = new SolidColorPaint(labelColor.WithAlpha(ZeroLineAlpha), 1),
                // 其余横向网格线不要：六行各画一组会在视觉上把整块面板切碎。
                SeparatorsPaint = null,
            },
        ];
    }

    /// <summary>
    /// 把主题资源里的纯色画刷取成 Skia 的颜色。找不到、或不是纯色画刷就退回兜底色 ——
    /// 主题缺一个键不该让面板炸掉，也不该让曲线直接消失。
    ///
    /// <para>
    /// <b>为什么是 <c>TryGetResource</c> 而不是 <c>TryFindResource</c>：</b>
    /// 后者是 <c>ResourceNodeExtensions</c> 上的<b>扩展方法</b>，而扩展方法不能以「不带接收者的裸名」
    /// 调用（写 <c>TryFindResource(...)</c> 编译器只在类成员里找，找不到就报 CS0103；
    /// 必须有接收者表达式，例如 <c>((IResourceHost)this).TryFindResource(...)</c>）。
    /// 前者是 <c>StyledElement</c> 上的<b>实例方法</b>，裸名可直接调，语义相同：
    /// 先看自己的 Resources，再沿逻辑树一路问到 Application。传 <c>ActualThemeVariant</c>
    /// 是为了让键落在 ThemeDictionaries 里时也解析得对。
    /// </para>
    /// <para>
    /// <b>主题里没有这些键也成立：</b>资源键是宿主设计系统的约定（<c>Theme/Tokens/Semantic.axaml</c>），
    /// 本控件把宿主当「可选的上游」看待 —— 有键就跟随主题，没键就用兜底色，绝不因为少一个键而抛异常。
    /// </para>
    /// </summary>
    private SKColor ThemeColor(string key, SKColor fallback)
        => TryGetResource(key, ActualThemeVariant, out var value) && value is ISolidColorBrush brush
            ? new SKColor(brush.Color.R, brush.Color.G, brush.Color.B)
            : fallback;
}
