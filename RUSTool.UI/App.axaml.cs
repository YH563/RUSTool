using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RUSTool.Communication;
using RUSTool.Services.Logging;
using RUSTool.Services.Robot;
using RUSTool.UI.Services.Logging;
using RUSTool.UI.ViewModels;
using RUSTool.UI.Views;
using RUSTool.Visualization.Logging;
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

        // 图形栈的日志也接到同一个日志器：库内部的 ILogger 门面默认没有任何 provider，
        // 不接这一步，URDF 资产解析 / 网格导入 / 加载失败就只存在于库内部（面板与落盘文件里一条都看不到）。
        // 必须在这里做 —— 库的 Logger.Initialize 只生效一次，晚于第一条库日志就接不上了。
        SimulationLogBridge.Attach(new SimulationLogSink(log));
        log.Log("3D 图形栈日志已接入（来源 sim，最低等级 Debug）", LogLevel.Debug, "sim");

        IRobotService robot = new RobotService(bridge);
        var session = new RobotSession();

        // 允许 `--clinical` 直接以临床模式启动，方便反复对照两种界面。
        return new MainViewModel(robot, session, log)
        {
            IsDebugMode = !args.Contains("--clinical"),
        };
    }
}

