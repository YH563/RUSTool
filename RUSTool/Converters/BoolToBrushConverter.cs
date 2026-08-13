using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace RUSTool.Converters;

/// <summary>布尔值 → 颜色刷：true=绿色（完成），false=灰色（未完成）。</summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public static readonly BoolToBrushConverter Instance = new();

    private static readonly IBrush Done = new SolidColorBrush(Color.Parse("#4CAF50"));
    private static readonly IBrush Pending = new SolidColorBrush(Color.Parse("#D0D0D0"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Done : Pending;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
