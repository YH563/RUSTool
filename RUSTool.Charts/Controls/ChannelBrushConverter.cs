using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace RUSTool.Charts.Controls;

/// <summary>
/// 通道下标 → 该路数据的颜色画刷（<see cref="StatePalette.BrushFor"/>）：行头那条色标用它上色。
///
/// <para>
/// <b>为什么需要一个转换器：</b>色标写在 XAML 里，而 XAML 的 <c>Fill</c> 要的是
/// <see cref="Avalonia.Media.IBrush"/>；颜色表本身是按 Skia 颜色定义、给曲线用的。
/// 转换器只是把同一个色从表的另一面取出来，不产生第二个配色来源
/// （两边同表见 <see cref="StatePalette"/>）。
/// </para>
/// <para>
/// <b>只做单向：</b>没有任何人从颜色反推通道 —— 反向转换直接抛，免得哪天被接上了，
/// 变成「改色标就能改数据」这种没人想得通的行为。
/// </para>
/// </summary>
public sealed class ChannelBrushConverter : IValueConverter
{
    /// <inheritdoc />
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int channel ? StatePalette.BrushFor(channel) : null;

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">总是抛出：颜色只从通道下标算出来，没有反向。</exception>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("色标只从通道下标算出来，没有反向转换。");
}
