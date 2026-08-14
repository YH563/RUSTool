using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RUSTool.Communication;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
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
            var bridge = new BridgeClient();
            ILogService log = new LogService();
            // 所有后端指令的发送与结果都写入全局日志
            bridge.Logger = (message, isError) =>
                log.Log(message, isError ? LogLevel.Error : LogLevel.Info);
            IRobotService robot = new RobotService(bridge);
            var session = new RobotSession();
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(robot, session, log),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}