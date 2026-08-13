using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Services;
using System;
using System.Threading.Tasks;

namespace RUSTool.ViewModels;

/// <summary>
/// 仿真控制 VM（仅 Sim 驱动）：倍速、单步、仿真时间、帧率。
/// </summary>
public partial class SimulationViewModel : ViewModelBase
{
    private readonly IRobotService _robot;

    [ObservableProperty] private double _timeSpeed = 1.0;
    [ObservableProperty] private double _simTime;
    [ObservableProperty] private double _frameRate;
    [ObservableProperty] private string _statusText = "";

    public SimulationViewModel(IRobotService robot)
    {
        _robot = robot;
    }

    /// <summary>设置仿真倍速。</summary>
    [RelayCommand]
    private async Task SetTimeSpeed()
    {
        var r = await _robot.SetTimeSpeedAsync(TimeSpeed);
        StatusText = r.Success ? $"倍速已设为 {TimeSpeed:F1}x" : $"设置失败: {r.Message}";
    }

    /// <summary>单步仿真。</summary>
    [RelayCommand]
    private async Task StepOnce()
    {
        var r = await _robot.StepOnceAsync();
        StatusText = r.Success ? "已单步" : $"单步失败: {r.Message}";
    }

    /// <summary>刷新仿真时间 / 帧率 / 倍速。</summary>
    [RelayCommand]
    private async Task Refresh()
    {
        var speed = await _robot.GetTimeSpeedAsync();
        if (speed.Success && speed.Result.Length > 0)
            TimeSpeed = speed.Result[0];

        var time = await _robot.GetSimTimeAsync();
        if (time.Success && time.Result.Length > 0)
            SimTime = time.Result[0];

        var fps = await _robot.GetFrameRateAsync();
        if (fps.Success && fps.Result.Length > 0)
            FrameRate = fps.Result[0];

        StatusText = $"仿真时间 {SimTime:F1}s，帧率 {FrameRate:F0} fps";
    }
}
