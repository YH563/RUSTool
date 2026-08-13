using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RUSTool.Communication;
using RUSTool.Services;
using RUSTool.ViewModels;
using RUSTool.Views;

namespace RUSTool;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 依赖图组装（composition root）：传输 → 业务 → 共享状态/日志 → VM → View
            IRobotService robot = new RobotService(new BridgeClient());
            var session = new RobotSession();
            ILogService log = new LogService();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(robot, session, log),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}