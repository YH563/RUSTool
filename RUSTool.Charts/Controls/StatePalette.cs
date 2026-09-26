using Avalonia.Media;
using SkiaSharp;

namespace RUSTool.Charts.Controls;

/// <summary>
/// 曲线配色：每一路数据一个颜色（下标 = <c>RobotStateRow.ChannelIndex</c>）。
///
/// <para>
/// <b>为什么这几个 hex【不放】进主题库（宿主的 Tokens/Semantic）也不换成语义色：</b>
/// 它们回答的是「这条曲线是哪一路数据」（关节 1..6），而不是「这个控件处于什么状态」。
/// 同一路数据在浅色 / 深色下必须是同一个颜色，换主题不该改变曲线的含义 ——
/// 语义色恰好相反（同一根曲线换主题就该换色，那就认不出是哪一路了）。
/// </para>
/// <para>
/// <b>为什么是这一组：</b>线只有 ~1.4px 宽，六个颜色要在同一屏里彼此可分辨，
/// 所以色相均匀铺开（蓝 → 青 → 绿 → 黄 → 橙 → 紫）且明度接近，
/// 避免两个相邻色相（如两个蓝）同时出现在六行里。
/// </para>
/// <para>
/// <b>一种颜色两个表示（Skia 的 <see cref="SKColor"/> / XAML 的 <see cref="IBrush"/>），
/// 但只有一个来源：</b>曲线画在 Skia 画布上（<see cref="For"/>），行头那条色标画在 XAML 里
/// （<see cref="BrushFor"/>）—— 两者都出自下面这一张表（含兜底色与取模规则），
/// 所以「色标指着的那条线」不会跟线本身走成两个颜色。
/// </para>
/// <para>
/// <b>唯一来源就在这里：</b>想统一调色只改这一处。原先它们写在宿主的曲线卡片资源里
/// （<c>RUSTool.UI/Views/Debug/ChartPanel.axaml</c>，随图表栈搬进本工程后那个文件已下线）、
/// 由代码后置按槽位取；图表栈整体搬进本工程后，配色跟着曲线走 —— 谁画曲线谁定义线色。
/// </para>
/// </summary>
internal static class StatePalette
{
    /// <summary>曲线线宽（px）：比 1px 的轴线粗一点，六行并排时还看得清，又不糊成一条带。</summary>
    public const float StrokeWidth = 1.4f;

    /// <summary>六路数据的配色（顺序 = 通道下标）。</summary>
    private static readonly SKColor[] ChannelColors =
    [
        new(0x2E, 0x90, 0xFA), // 蓝
        new(0x06, 0xAE, 0xD4), // 青
        new(0x12, 0xB7, 0x6A), // 绿
        new(0xEA, 0xAA, 0x08), // 黄
        new(0xF7, 0x90, 0x09), // 橙
        new(0x9E, 0x77, 0xED), // 紫
    ];

    /// <summary>取不到配色时的兜底颜色（中性灰：至少能看出形状，而不是曲线隐身）。</summary>
    private static readonly SKColor FallbackColor = new(0x98, 0xA2, 0xB3);

    /// <summary>兜底色对应的画刷（与 <see cref="FallbackColor"/> 是同一个灰）。</summary>
    private static readonly IBrush FallbackBrush = ToBrush(FallbackColor);

    /// <summary>
    /// 同一张表的画刷表示（行头色标用）。造一次就固化：画刷在这里是跨行复用的绘制对象，
    /// 固化之后就不用担心「谁改了谁」，也不必每次绑定都 new 一个。
    /// </summary>
    private static readonly IBrush[] ChannelBrushes = CreateBrushes();

    /// <summary>
    /// 取第 <paramref name="channel"/> 路数据的颜色。超出配色表就<b>绕回来</b>（取模）而不是给灰色：
    /// 目录比配色长这件事是调用方的选择，不该表现为「多出来的通道全是同一种灰」。
    /// </summary>
    public static SKColor For(int channel)
    {
        if (channel < 0)
            return FallbackColor;

        return ChannelColors[channel % ChannelColors.Length];
    }

    /// <summary>
    /// 取第 <paramref name="channel"/> 路数据的颜色画刷（行头色标用）—— 与 <see cref="For"/>
    /// 同表同规则（兜底与取模都在这里走一遍），界面里的色标因此永远和线上的颜色是一个色。
    /// </summary>
    public static IBrush BrushFor(int channel)
        => channel < 0 ? FallbackBrush : ChannelBrushes[channel % ChannelBrushes.Length];

    /// <summary>把 <see cref="ChannelColors"/> 逐个转成画刷。</summary>
    private static IBrush[] CreateBrushes()
    {
        var brushes = new IBrush[ChannelColors.Length];
        for (var i = 0; i < brushes.Length; i++)
            brushes[i] = ToBrush(ChannelColors[i]);

        return brushes;
    }

    /// <summary>Skia 颜色 → Avalonia 画刷（两边的 RGB 相同，曲线与色标才是同一个色）。</summary>
    private static IBrush ToBrush(SKColor color)
        => new SolidColorBrush(Color.FromRgb(color.Red, color.Green, color.Blue)).ToImmutable();
}
