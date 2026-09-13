using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RUSTool.Communication;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using RUSTool.UI.Services.Logging;
using RUSTool.UI.ViewModels;
using RUSTool.UI.Views;
using System;
using System.Linq;

namespace RUSTool.UI;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? Array.Empty<string>();

            // 截图模式由 Program 自己建窗口（而且不接后端），这里不能重复建。
            if (!args.Contains("--shot"))
            {
                desktop.MainWindow = new MainWindow { DataContext = CreateMainViewModel(args) };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 依赖图组装（composition root）：传输 → 业务 → 共享状态 / 日志 → VM → View。
    ///
    /// <para>
    /// 全项目【唯一】new 具体实现的地方。所有 ViewModel 只依赖 Core 里的接口
    /// （<see cref="IRobotService"/> / <see cref="ILogService"/> / <see cref="RobotSession"/>），
    /// 因此换后端（真机 / 仿真 / 回放）只需要改这一处。
    /// </para>
    /// <para>
    /// <c>internal</c> 是为了让截图模式（<c>Program.Capture</c>）复用同一份组装 ——
    /// 截图里的服务拓扑与真实运行时完全一致，不会"预览一个不存在的界面"。
    /// </para>
    /// </summary>
    internal static MainViewModel CreateMainViewModel(string[] args)
    {
        var bridge = new BridgeClient();
        ILogService log = new LogService();

        // 所有后端指令的发送与结果都写进全局日志（同时落盘，便于事后追溯）。
        bridge.Logger = (message, isError) =>
            log.Log(message, isError ? LogLevel.Error : LogLevel.Info, "bridge");

        IRobotService robot = new RobotService(bridge);
        var session = new RobotSession();

        // 允许 `--clinical` 直接以临床模式启动，方便反复对照两种界面。
        return new MainViewModel(robot, session, log)
        {
            IsDebugMode = !args.Contains("--clinical"),
        };
    }
}

