using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using RUSTool.Communication;
using RUSTool.Services;
using System;

namespace RUSTool.ViewModels;

/// <summary>
/// 机械臂状态 HUD VM：订阅 /state 状态流，把状态帧转换成 UI 可绑定的属性。
/// 状态帧在后台线程到达，故统一 Post 到 UI 线程更新。
/// </summary>
public partial class RobotStatusViewModel : ViewModelBase
{
    private readonly IRobotService _robot;

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private double _frameRate;

    // 关节角 (deg)
    [ObservableProperty] private double _joint1;
    [ObservableProperty] private double _joint2;
    [ObservableProperty] private double _joint3;
    [ObservableProperty] private double _joint4;
    [ObservableProperty] private double _joint5;
    [ObservableProperty] private double _joint6;

    // 法兰位姿 (m / deg)
    [ObservableProperty] private double _flangeX;
    [ObservableProperty] private double _flangeY;
    [ObservableProperty] private double _flangeZ;
    [ObservableProperty] private double _flangeRx;
    [ObservableProperty] private double _flangeRy;
    [ObservableProperty] private double _flangeRz;

    // 关节力矩 (Nm)
    [ObservableProperty] private double _torque1;
    [ObservableProperty] private double _torque2;
    [ObservableProperty] private double _torque3;
    [ObservableProperty] private double _torque4;
    [ObservableProperty] private double _torque5;
    [ObservableProperty] private double _torque6;

    public RobotStatusViewModel(IRobotService robot)
    {
        _robot = robot;
        _robot.ConnectionChanged += connected => IsConnected = connected;
        _robot.StateUpdated += OnStateUpdated;
    }

    private void OnStateUpdated(BridgeProtocol.StateFrame state)
    {
        Dispatcher.UIThread.Post(() =>
        {
            Joint1 = Rad2Deg(state.JointPos.Length > 0 ? state.JointPos[0] : 0);
            Joint2 = Rad2Deg(state.JointPos.Length > 1 ? state.JointPos[1] : 0);
            Joint3 = Rad2Deg(state.JointPos.Length > 2 ? state.JointPos[2] : 0);
            Joint4 = Rad2Deg(state.JointPos.Length > 3 ? state.JointPos[3] : 0);
            Joint5 = Rad2Deg(state.JointPos.Length > 4 ? state.JointPos[4] : 0);
            Joint6 = Rad2Deg(state.JointPos.Length > 5 ? state.JointPos[5] : 0);

            FlangeX = state.FlangePos.Length > 0 ? state.FlangePos[0] : 0;
            FlangeY = state.FlangePos.Length > 1 ? state.FlangePos[1] : 0;
            FlangeZ = state.FlangePos.Length > 2 ? state.FlangePos[2] : 0;
            FlangeRx = Rad2Deg(state.FlangePos.Length > 3 ? state.FlangePos[3] : 0);
            FlangeRy = Rad2Deg(state.FlangePos.Length > 4 ? state.FlangePos[4] : 0);
            FlangeRz = Rad2Deg(state.FlangePos.Length > 5 ? state.FlangePos[5] : 0);

            Torque1 = state.Effort.Length > 0 ? state.Effort[0] : 0;
            Torque2 = state.Effort.Length > 1 ? state.Effort[1] : 0;
            Torque3 = state.Effort.Length > 2 ? state.Effort[2] : 0;
            Torque4 = state.Effort.Length > 3 ? state.Effort[3] : 0;
            Torque5 = state.Effort.Length > 4 ? state.Effort[4] : 0;
            Torque6 = state.Effort.Length > 5 ? state.Effort[5] : 0;

            FrameRate = state.FrameRate;
        });
    }

    private static double Rad2Deg(double rad) => rad * 180 / Math.PI;
}
