using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Models;
using RUSTool.Services;
using System;
using System.Threading.Tasks;

namespace RUSTool.ViewModels.Robot;

public partial class RobotViewModel : ViewModelBase
{
    private readonly ICommandService _service;

    // 服务器地址
    [ObservableProperty] private string _serverUrl = "ws://localhost:8765";
    // 错误信息（显示在界面上）
    [ObservableProperty] private string _errorMessage = "";

    // 状态
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private double _frameRate;

    // 关节角 (rad)
    [ObservableProperty] private double _joint1;
    [ObservableProperty] private double _joint2;
    [ObservableProperty] private double _joint3;
    [ObservableProperty] private double _joint4;
    [ObservableProperty] private double _joint5;
    [ObservableProperty] private double _joint6;

    // 法兰位姿 (m / rad)
    [ObservableProperty] private double _flangeX;
    [ObservableProperty] private double _flangeY;
    [ObservableProperty] private double _flangeZ;
    [ObservableProperty] private double _flangeRx;
    [ObservableProperty] private double _flangeRy;
    [ObservableProperty] private double _flangeRz;

    // 点动参数
    [ObservableProperty] private int _jogSpeed = 30;
    [ObservableProperty] private int _jogAcc = 30;
    [ObservableProperty] private int _jogRefFrame = 2;   // 0=关节, 2=基坐标, 4=工具
    [ObservableProperty] private double _jogMaxDis;  // 0=无限

    /// <summary>
    /// 构造，可注入 ICommandService（不传则默认使用 WebSocket 实现）
    /// </summary>
    public RobotViewModel(ICommandService? service = null)
    {
        _service = service ?? new CommandService();
        _service.OnStateUpdated += OnStateUpdated;
        _service.OnConnected += () => IsConnected = true;
        _service.OnDisconnected += () => IsConnected = false;
        _service.OnError += msg => App.Current!.Dispatcher.Post(() =>
            ErrorMessage = msg);
    }

    /// <summary>
    /// 连接机器人
    /// </summary>
    [RelayCommand]
    private async Task ConnectToRobot()
    {
        ErrorMessage = "";
        try
        {
            await _service.ConnectAsync(ServerUrl);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"连接失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 断开连接
    /// </summary>
    [RelayCommand]
    private async Task DisconnectFromRobot() => await _service.DisconnectAsync();

    /// <summary>
    /// 关节空间运动（输入格式: 0.1,-0.5,1.2,0,0.3,0）
    /// </summary>
    [RelayCommand]
    private async Task MoveJ(string input)
    {
        try
        {
            var joints = Array.ConvertAll(input.Split(',', StringSplitOptions.TrimEntries), double.Parse);
            await _service.SendMoveJointAsync(joints);
        }
        catch
        {
            ErrorMessage = "MoveJ 参数格式错误，示例: 0.1,-0.5,1.2,0,0.3,0";
        }
    }

    /// <summary>
    /// 笛卡尔直线运动（输入格式: x,y,z,rx,ry,rz）
    /// </summary>
    [RelayCommand]
    private async Task MoveL(string input)
    {
        try
        {
            var pose = Array.ConvertAll(input.Split(',', StringSplitOptions.TrimEntries), double.Parse);
            await _service.SendMoveLinearAsync(pose);
        }
        catch
        {
            ErrorMessage = "MoveL 参数格式错误，示例: 0.3,0,0.5,3.14,0,0";
        }
    }

    // 辅助
    private Task JogJoint(int nb, int dir) =>
        _service.SendJogStartAsync(0, nb, dir, JogSpeed, JogAcc, JogMaxDis);

    private Task JogCart(int nb, int dir) =>
        _service.SendJogStartAsync(JogRefFrame, nb, dir, JogSpeed, JogAcc, JogMaxDis);

    // 关节点动 (强制 ref=0)
    [RelayCommand] private Task JogAxis1Pos() => JogJoint(1, 1);
    [RelayCommand] private Task JogAxis1Neg() => JogJoint(1, 0);
    [RelayCommand] private Task JogAxis2Pos() => JogJoint(2, 1);
    [RelayCommand] private Task JogAxis2Neg() => JogJoint(2, 0);
    [RelayCommand] private Task JogAxis3Pos() => JogJoint(3, 1);
    [RelayCommand] private Task JogAxis3Neg() => JogJoint(3, 0);
    [RelayCommand] private Task JogAxis4Pos() => JogJoint(4, 1);
    [RelayCommand] private Task JogAxis4Neg() => JogJoint(4, 0);
    [RelayCommand] private Task JogAxis5Pos() => JogJoint(5, 1);
    [RelayCommand] private Task JogAxis5Neg() => JogJoint(5, 0);
    [RelayCommand] private Task JogAxis6Pos() => JogJoint(6, 1);
    [RelayCommand] private Task JogAxis6Neg() => JogJoint(6, 0);

    // 笛卡尔点动 (使用 JogRefFrame)
    [RelayCommand] private Task JogXPos() => JogCart(1, 1);
    [RelayCommand] private Task JogXNeg() => JogCart(1, 0);
    [RelayCommand] private Task JogYPos() => JogCart(2, 1);
    [RelayCommand] private Task JogYNeg() => JogCart(2, 0);
    [RelayCommand] private Task JogZPos() => JogCart(3, 1);
    [RelayCommand] private Task JogZNeg() => JogCart(3, 0);
    [RelayCommand] private Task JogRxPos() => JogCart(4, 1);
    [RelayCommand] private Task JogRxNeg() => JogCart(4, 0);
    [RelayCommand] private Task JogRyPos() => JogCart(5, 1);
    [RelayCommand] private Task JogRyNeg() => JogCart(5, 0);
    [RelayCommand] private Task JogRzPos() => JogCart(6, 1);
    [RelayCommand] private Task JogRzNeg() => JogCart(6, 0);

    // 停止
    [RelayCommand] private Task StopJog() => _service.SendJogStopAsync();

    [RelayCommand] private Task StopJogImmediate() => _service.SendJogStopImmediateAsync();

    /// <summary>
    /// 处理 WebSocket 推送的状态
    /// </summary>
    private void OnStateUpdated(RobotState state)
    {
        App.Current!.Dispatcher.Post(() =>
        {
            Joint1 = state.JointPos.Length > 0 ? state.JointPos[0] : 0;
            Joint2 = state.JointPos.Length > 1 ? state.JointPos[1] : 0;
            Joint3 = state.JointPos.Length > 2 ? state.JointPos[2] : 0;
            Joint4 = state.JointPos.Length > 3 ? state.JointPos[3] : 0;
            Joint5 = state.JointPos.Length > 4 ? state.JointPos[4] : 0;
            Joint6 = state.JointPos.Length > 5 ? state.JointPos[5] : 0;

            FlangeX = state.FlangePos.Length > 0 ? state.FlangePos[0] : 0;
            FlangeY = state.FlangePos.Length > 1 ? state.FlangePos[1] : 0;
            FlangeZ = state.FlangePos.Length > 2 ? state.FlangePos[2] : 0;
            FlangeRx = state.FlangePos.Length > 3 ? state.FlangePos[3] : 0;
            FlangeRy = state.FlangePos.Length > 4 ? state.FlangePos[4] : 0;
            FlangeRz = state.FlangePos.Length > 5 ? state.FlangePos[5] : 0;

            FrameRate = state.FrameRate;
        });
    }
}
