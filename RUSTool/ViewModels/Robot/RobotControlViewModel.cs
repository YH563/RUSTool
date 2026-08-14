using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using System;
using System.Threading.Tasks;

namespace RUSTool.ViewModels.Robot;

/// <summary>
/// 机械臂手动控制 VM：MoveJ / MoveL、点动（开始/结束）、停止、全局急停。
/// 只依赖 IRobotService，不接触协议字符串与 BridgeClient。
/// </summary>
public partial class RobotControlViewModel : ViewModelBase
{
    private readonly IRobotService _robot;
    private readonly ILogService _log;

    // 服务器连接状态（只读，连接动作由 ConnectionViewModel 负责）
    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private string _errorMessage = "";

    // 点动参数
    [ObservableProperty] private int _jogSpeed = 30;
    [ObservableProperty] private int _jogAcc = 30;
    [ObservableProperty] private int _jogRefFrame = 2;   // 0=关节, 2=基坐标, 4=工具
    [ObservableProperty] private double _jogMaxDistance; // 0=无限

    public RobotControlViewModel(IRobotService robot, ILogService log)
    {
        _robot = robot;
        _log = log;
        _robot.ConnectionChanged += connected => IsConnected = connected;
    }

    /// <summary>关节空间运动（输入格式: 度值 0,45,-30,0,90,0，自动转换为弧度下发）。</summary>
    [RelayCommand]
    private async Task MoveJ(string input)
    {
        try
        {
            var joints = Array.ConvertAll(input.Split(',', StringSplitOptions.TrimEntries), double.Parse);
            for (var i = 0; i < joints.Length; i++)
                joints[i] *= Math.PI / 180.0;
            var result = await _robot.MoveJAsync(joints);
            if (!result.Success)
            {
                ErrorMessage = $"MoveJ 失败: {result.Message}";
                _log.Log($"MoveJ 失败: {result.Message}", LogLevel.Error);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "MoveJ 参数格式错误，示例: 0,45,-30,0,90,0 (角度)";
            _log.Log($"MoveJ 参数格式错误: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>笛卡尔直线运动（输入格式: x,y,z,rx,ry,rz）。</summary>
    [RelayCommand]
    private async Task MoveL(string input)
    {
        try
        {
            var pose = Array.ConvertAll(input.Split(',', StringSplitOptions.TrimEntries), double.Parse);
            var result = await _robot.MoveLAsync(pose);
            if (!result.Success)
            {
                ErrorMessage = $"MoveL 失败: {result.Message}";
                _log.Log($"MoveL 失败: {result.Message}", LogLevel.Error);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "MoveL 参数格式错误，示例: 0.3,0,0.5,3.14,0,0";
            _log.Log($"MoveL 参数格式错误: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>减速停止点动。</summary>
    [RelayCommand]
    private Task StopJog() => _robot.StopJogAsync();

    /// <summary>立即停止点动。</summary>
    [RelayCommand]
    private Task StopJogImmediate() => _robot.StopJogImmediateAsync();

    /// <summary>全局急停：停止所有运动（任务级 stop）。</summary>
    [RelayCommand]
    private Task StopAllMotion() => _robot.StopAsync();

    /// <summary>急停恢复：清除急停状态，恢复运动。</summary>
    [RelayCommand]
    private Task Reset() => _robot.ResetAsync();
    
    /// <summary>
    /// 暂停运动
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private Task Pause() => _robot.PauseAsync();
    
    /// <summary>
    /// 恢复运动
    /// </summary>
    /// <returns></returns>
    [RelayCommand]
    private Task Resume() => _robot.ResumeAsync();

    /// <summary>点动开始（按下）：按当前参考系下发。</summary>
    public void BeginJog(int axis, int direction)
    {
        _ = _robot.StartJogAsync(
            new JogParameters(JogRefFrame, axis, direction, JogSpeed, JogAcc, JogMaxDistance));
    }

    /// <summary>点动结束（松开）：减速停止。</summary>
    public void EndJog()
    {
        _ = _robot.StopJogAsync();
    }
}
