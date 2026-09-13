using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using System;

namespace RUSTool.UI.Views.Debug;

/// <summary>
/// 数据曲线面板。
///
/// <para>
/// 曲线目前是两条装饰性正弦。占位区若只有一句灰字，最容易被误读成
/// "这个功能没做完"或"渲染挂了"；把波形画出来至少说明"这里是画曲线的地方、
/// 曲线是彩色的、图例颜色与曲线颜色对应"。
/// 接入真实数据时只需把 <see cref="FillWave"/> 换成从数据流取点。
/// </para>
/// <para>
/// 颜色由 XAML 声明（资源 ChFx / ChFy），这里只填点 —— 避免同一份配色
/// 在 C# 与 XAML 里各写一遍。
/// </para>
/// </summary>
public partial class ChartPanel : UserControl
{
    public ChartPanel()
    {
        InitializeComponent();

        // (曲线, 幅值, 相位, 周期数)
        FillWave(WaveFx, 42, 0.0, 1.5);
        FillWave(WaveFy, 28, 1.1, 2.0);
    }

    private static void FillWave(Polyline line, double amplitude, double phase, double cycles)
    {
        const double width = 400;
        const double height = 160;
        const int steps = 240;

        line.Points.Clear();

        for (var i = 0; i <= steps; i++)
        {
            var t = (double)i / steps;
            var x = width * t;
            var y = height / 2 - amplitude * Math.Sin(t * cycles * Math.PI * 2 + phase);
            line.Points.Add(new Avalonia.Point(x, y));
        }
    }

    /// <summary>
    /// 图例勾选联动曲线显隐：勾选 → 曲线出现，取消 → 曲线消失。
    /// 没有联动的话，用户会以为程序坏了。
    /// </summary>
    private void OnChannelToggled(object? sender, RoutedEventArgs e) => SetChannel(sender, (sender as CheckBox)?.IsChecked == true);

    private void SetChannel(object? sender, bool visible)
    {
        if (sender is not CheckBox box)
        {
            return;
        }

        // 目前只有 Fx / Fy 画了曲线；其余通道先留空 ——
        // 等真实数据接入后按同样方式补上即可。
        switch (box.Content?.ToString())
        {
            case "Fx":
                BaseFx.IsVisible = visible;
                WaveFx.IsVisible = visible;
                break;
            case "Fy":
                BaseFy.IsVisible = visible;
                WaveFy.IsVisible = visible;
                break;
        }
    }
}
