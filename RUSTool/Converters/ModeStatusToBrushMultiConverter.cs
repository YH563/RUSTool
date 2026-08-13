using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Globalization;
using RUSTool.Services;

namespace RUSTool.Converters;

/// <summary>
/// 运动状态 → 状态灯颜色（MultiBinding: [0]=RobotMode, [1]=IsPaused）。
/// 空闲=灰，手动=蓝，扫查=绿，已暂停=橙。
/// </summary>
public sealed class ModeStatusToBrushMultiConverter : IMultiValueConverter
{
    public static readonly ModeStatusToBrushMultiConverter Instance = new();

    private static readonly IBrush Idle = new SolidColorBrush(Color.Parse("#9E9E9E"));
    private static readonly IBrush Manual = new SolidColorBrush(Color.Parse("#1A73E8"));
    private static readonly IBrush Scan = new SolidColorBrush(Color.Parse("#43A047"));
    private static readonly IBrush Paused = new SolidColorBrush(Color.Parse("#FB8C00"));

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is null || values.Count < 2)
            return Idle;

        // 暂停优先（橙色）
        if (values[1] is true)
            return Paused;

        return values[0] switch
        {
            RobotMode.Manual => Manual,
            RobotMode.Scan => Scan,
            _ => Idle
        };
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

