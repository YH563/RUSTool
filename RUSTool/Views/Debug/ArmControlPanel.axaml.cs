using Avalonia.Controls;
using Avalonia.Input;
using RUSTool.ViewModels.Robot;

namespace RUSTool.Views.Debug;

public partial class ArmControlPanel : UserControl
{
    public ArmControlPanel()
    {
        InitializeComponent();

        // 点动按钮：用 AddHandler 捕获 Button 内部已处理的指针事件
        var jogBtns = new[] {
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
        if (sender is ComboBox { SelectedIndex: var idx } && DataContext is RobotViewModel vm)
        {
            vm.JogRefFrame = idx switch
            {
                0 => 2,  // 基坐标系
                1 => 4,  // 工具坐标系
                2 => 0,  // 关节空间
                _ => 2,
            };

            // 切换标签文字
            if (idx == 2) // 关节空间
            {
                LabelX.Text = "关节1";
                LabelY.Text = "关节2";
                LabelZ.Text = "关节3";
                LabelRx.Text = "关节4";
                LabelRy.Text = "关节5";
                LabelRz.Text = "关节6";
            }
            else
            {
                LabelX.Text = "X方向";
                LabelY.Text = "Y方向";
                LabelZ.Text = "Z方向";
                LabelRx.Text = "绕X轴旋转";
                LabelRy.Text = "绕Y轴旋转";
                LabelRz.Text = "绕Z轴旋转";
            }
        }
    }

    private void OnJogPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Button { Tag: string tag } && DataContext is RobotViewModel vm)
        {
            // 关节空间模式 → 映射到 J1~J6
            if (vm.JogRefFrame == 0)
            {
                var jointCmd = tag switch
                {
                    "X+"  => vm.JogAxis1PosCommand,  "X-"  => vm.JogAxis1NegCommand,
                    "Y+"  => vm.JogAxis2PosCommand,  "Y-"  => vm.JogAxis2NegCommand,
                    "Z+"  => vm.JogAxis3PosCommand,  "Z-"  => vm.JogAxis3NegCommand,
                    "Rx+" => vm.JogAxis4PosCommand,  "Rx-" => vm.JogAxis4NegCommand,
                    "Ry+" => vm.JogAxis5PosCommand,  "Ry-" => vm.JogAxis5NegCommand,
                    "Rz+" => vm.JogAxis6PosCommand,  "Rz-" => vm.JogAxis6NegCommand,
                    _ => null,
                };
                jointCmd?.Execute(null);
            }
            else
            {
                // 笛卡尔模式（基坐标/工具坐标）
                var cmd = tag switch
                {
                    "X+" => vm.JogXPosCommand,    "X-" => vm.JogXNegCommand,
                    "Y+" => vm.JogYPosCommand,    "Y-" => vm.JogYNegCommand,
                    "Z+" => vm.JogZPosCommand,    "Z-" => vm.JogZNegCommand,
                    "Rx+" => vm.JogRxPosCommand,  "Rx-" => vm.JogRxNegCommand,
                    "Ry+" => vm.JogRyPosCommand,  "Ry-" => vm.JogRyNegCommand,
                    "Rz+" => vm.JogRzPosCommand,  "Rz-" => vm.JogRzNegCommand,
                    _ => null,
                };
                cmd?.Execute(null);
            }
        }
    }

    private void OnJogReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (DataContext is RobotViewModel vm)
        {
            vm.StopJogCommand.Execute(null);
        }
    }
}
