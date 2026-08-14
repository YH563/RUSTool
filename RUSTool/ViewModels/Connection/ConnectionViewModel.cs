using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using System;
using System.Threading.Tasks;

namespace RUSTool.ViewModels.Connection;

/// <summary>
/// 连接 / 驱动 VM：连接、断开、上使能、切换驱动、状态查询。
/// 是连接动作的唯一入口（其他 VM 只读连接状态）。
/// </summary>
public partial class ConnectionViewModel : ViewModelBase
{
    private readonly IRobotService _robot;
    private readonly RobotSession _session;
    private readonly ILogService _log;

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _statusText = "未连接";
    [ObservableProperty] private string _errorMessage = "";

    /// <summary>当前驱动：0=真实，1=仿真。</summary>
    [ObservableProperty] private int _selectedDriver = 1;

    public ConnectionViewModel(IRobotService robot, RobotSession session, ILogService log)
    {
        _robot = robot;
        _session = session;
        _log = log;
        _session.Driver = SelectedDriver;
        _robot.ConnectionChanged += connected =>
        {
            IsConnected = connected;
            _session.IsConnected = connected;
            StatusText = connected ? "已连接" : "未连接";
        };
    }

    partial void OnSelectedDriverChanged(int value) => _session.Driver = value;

    /// <summary>连接（连 /control，成功后开启状态流并自动上使能）。</summary>
    [RelayCommand]
    private async Task Connect()
    {
        ErrorMessage = "";
        StatusText = "连接中…";
        try
        {
            await _robot.ConnectAsync();
            _robot.StartStateStream();
            StatusText = "已连接";
            await AutoEnableAsync();
        }
        catch (Exception ex)
        {
            StatusText = "连接失败";
            ErrorMessage = ex.Message;
            _log.Log($"连接失败: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>连接成功后自动上使能，并刷新使能状态（失败不阻断连接）。</summary>
    private async Task AutoEnableAsync()
    {
        try
        {
            var r = await _robot.RobotEnableAsync(1);
            IsEnabled = r.Success;
            _session.IsEnabled = r.Success;
            if (!r.Success)
            {
                ErrorMessage = $"上使能失败: {r.Message}";
                _log.Log($"上使能失败: {r.Message}", LogLevel.Error);
            }
        }
        catch (Exception ex)
        {
            IsEnabled = false;
            _session.IsEnabled = false;
            ErrorMessage = $"上使能失败: {ex.Message}";
            _log.Log($"上使能失败: {ex.Message}", LogLevel.Error);
        }
    }

    /// <summary>断开连接。</summary>
    [RelayCommand]
    private void Disconnect()
    {
        _robot.StopStateStream();
        _robot.Disconnect();
        IsConnected = false;
        IsEnabled = false;
        _session.IsConnected = false;
        _session.IsEnabled = false;
        StatusText = "未连接";
    }

    /// <summary>上使能。</summary>
    [RelayCommand]
    private async Task EnableRobot()
    {
        var r = await _robot.RobotEnableAsync(1);
        IsEnabled = r.Success;
        _session.IsEnabled = r.Success;
        ErrorMessage = r.Success ? "" : $"上使能失败: {r.Message}";
        if (!r.Success)
            _log.Log($"上使能失败: {r.Message}", LogLevel.Error);
    }

    /// <summary>下使能。</summary>
    [RelayCommand]
    private async Task DisableRobot()
    {
        var r = await _robot.RobotEnableAsync(0);
        if (r.Success)
            IsEnabled = false;
        else
        {
            ErrorMessage = $"下使能失败: {r.Message}";
            _log.Log($"下使能失败: {r.Message}", LogLevel.Error);
        }
        _session.IsEnabled = IsEnabled;
    }

    /// <summary>切换驱动（真实/仿真）。</summary>
    [RelayCommand]
    private async Task SwitchDriver()
    {
        var r = await _robot.SwitchDriverAsync(SelectedDriver);
        if (!r.Success)
        {
            ErrorMessage = $"切换驱动失败: {r.Message}";
            _log.Log($"切换驱动失败: {r.Message}", LogLevel.Error);
        }
    }

    /// <summary>按参数切换驱动（菜单用，0=真实，1=仿真）。</summary>
    [RelayCommand]
    private async Task SwitchToDriver(int driver)
    {
        SelectedDriver = driver;
        _session.Driver = driver;
        var r = await _robot.SwitchDriverAsync(driver);
        if (!r.Success)
        {
            ErrorMessage = $"切换驱动失败: {r.Message}";
            _log.Log($"切换驱动失败: {r.Message}", LogLevel.Error);
        }
    }

    /// <summary>查询连接状态。</summary>
    [RelayCommand]
    private async Task RefreshConnection()
    {
        var r = await _robot.QueryIsConnectedAsync();
        if (r.Success && r.Result.Length > 0)
        {
            IsConnected = r.Result[0] != 0;
            _session.IsConnected = IsConnected;
        }
    }
}
