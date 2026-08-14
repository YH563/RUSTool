using Avalonia.Controls;
using Avalonia.Input;
using RUSTool.ViewModels.Robot;

namespace RUSTool.Views.Debug;

public partial class ArmControlPanel : UserControl
{
    public ArmControlPanel()
    {
        InitializeComponent();

        // 点动按钮：按下开始 / 松开停止。用 AddHandler 捕获 Button 内部已处理的指针事件。
        var jogBtns = new[]
        {
            BtnXNeg, BtnXPos, BtnYNeg, BtnYPos, BtnZNeg, BtnZPos,
            BtnRxNeg, BtnRxPos, BtnRyNeg, BtnRyPos, BtnRzNeg, BtnRzPos,
        };
        foreach (var btn in jogBtns)
        {
            btn.AddHandler(PointerPressedEvent, OnJogPressed, handledEventsToo: true);
            btn.AddHandler(PointerReleasedEvent, OnJogReleased, handledEventsToo: true);
        }
    }

    private void OnJogModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedIndex: var idx } && DataContext is RobotControlViewModel vm)
        {
            vm.JogRefFrame = idx switch
            {
                0 => 2,  // 基坐标系
                1 => 4,  // 工具坐标系
                2 => 0,  // 关节空间
                _ => 2,
            };

            var joint = idx == 2;
            LabelX.Text = joint ? "关节1" : "X方向";
            LabelY.Text = joint ? "关节2" : "Y方向";
            LabelZ.Text = joint ? "关节3" : "Z方向";
            LabelRx.Text = joint ? "关节4" : "绕X轴旋转";
            LabelRy.Text = joint ? "关节5" : "绕Y轴旋转";
            LabelRz.Text = joint ? "关节6" : "绕Z轴旋转";
        }
    }

    private void OnJogPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && DataContext is RobotControlViewModel vm)
        {
            var (axis, dir) = tag switch
            {
                "X+" => (1, 1), "X-" => (1, 0),
                "Y+" => (2, 1), "Y-" => (2, 0),
                "Z+" => (3, 1), "Z-" => (3, 0),
                "Rx+" => (4, 1), "Rx-" => (4, 0),
                "Ry+" => (5, 1), "Ry-" => (5, 0),
                "Rz+" => (6, 1), "Rz-" => (6, 0),
                _ => (0, 0),
            };
            if (axis > 0)
                vm.BeginJog(axis, dir);
        }
    }

    private void OnJogReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is RobotControlViewModel vm)
            vm.EndJog();
    }
}
