using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RUSTool.ViewModels.Robot;

namespace RUSTool.ViewModels;

public enum WorkspaceMode
{
    Debug,
    Clinical
}

public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty]
    private WorkspaceMode _currentMode = WorkspaceMode.Debug;

    public RobotViewModel Robot { get; } = new();

    public bool IsDebugMode => CurrentMode == WorkspaceMode.Debug;
    public bool IsClinicalMode => CurrentMode == WorkspaceMode.Clinical;

    partial void OnCurrentModeChanged(WorkspaceMode value)
    {
        OnPropertyChanged(nameof(IsDebugMode));
        OnPropertyChanged(nameof(IsClinicalMode));
    }

    [RelayCommand]
    private void SwitchToDebug() => CurrentMode = WorkspaceMode.Debug;

    [RelayCommand]
    private void SwitchToClinical() => CurrentMode = WorkspaceMode.Clinical;
}