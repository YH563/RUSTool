using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using RUSTool.UI.ViewModels;

namespace RUSTool.UI.Views.Debug;

/// <summary>
/// 点动轴控件的交互部分（「手动画板」与「扫查面板」共用）。
///
/// <para>
/// 真机点动是【按住走、松手停】，Button 的 Click 命令表达不了这个语义，所以直接接指针事件：
/// 按下下发 <c>start_jog</c>，松开下发 <c>stop_jog_decel</c>。12 个按钮由 DataTemplate 生成，
/// 因此在容器上装【隧道阶段】处理器统一捕获，再从事件源向上反查轴与方向 —— 一份逻辑覆盖全部按钮。
/// </para>
/// </summary>
public partial class JogAxes : UserControl
{
    public JogAxes()
    {
        InitializeComponent();

        // Tunnel：先于 Button 自身拿到事件；handledEventsToo：即使被标记为已处理也不漏。
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(PointerCaptureLostEvent, OnPointerCaptureLost, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var (axis, direction) = FindTarget(e.Source);
        if (axis is null || DataContext is not RobotControlViewModel vm)
            return;

        _ = vm.BeginJogAsync(axis.Axis, direction);
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var (axis, _) = FindTarget(e.Source);
        if (axis is null || DataContext is not RobotControlViewModel vm)
            return;

        _ = vm.EndJogAsync();
    }

    /// <summary>指针捕获被系统抢走（拖出窗口、弹对话框…）时也要停 —— 否则机械臂会一直走下去。</summary>
    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (DataContext is RobotControlViewModel vm)
            _ = vm.EndJogAsync();
    }

    /// <summary>从事件源向上找点动按钮，取出轴与方向（Tag = "pos" / "neg"）。</summary>
    private static (JogAxis? Axis, int Direction) FindTarget(object? source)
    {
        if (source is not Visual visual)
            return (null, 0);

        for (Visual? current = visual; current is not null; current = current.GetVisualParent())
        {
            if (current is Button { Tag: string tag, DataContext: JogAxis axis })
                return (axis, tag == "pos" ? 1 : 0);
        }

        return (null, 0);
    }
}
