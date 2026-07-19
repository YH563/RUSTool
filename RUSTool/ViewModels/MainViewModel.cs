using CommunityToolkit.Mvvm.ComponentModel;
using RUSTool.ViewModels.Robot;

namespace RUSTool.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public RobotViewModel Robot { get; } = new();
}