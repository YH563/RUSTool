using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Styling;
using Avalonia.Threading;
using RUSTool.UI.Services;
using RUSTool.UI.ViewModels;
using RUSTool.UI.Views;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RUSTool.UI;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 截图模式：不打开真实窗口，直接把界面渲染成 PNG（见 preview.sh）。
        var shotIndex = Array.IndexOf(args, "--shot");
        if (shotIndex >= 0)
        {
            var outPath = shotIndex + 1 < args.Length && !args[shotIndex + 1].StartsWith("--")
                ? args[shotIndex + 1]
                : "showcase.png";
            Capture(outPath, args);
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                // 强制使用原生 Windows OpenGL (WGL)
                RenderingMode = new List<Win32RenderingMode>
                {
                Win32RenderingMode.Wgl,
                Win32RenderingMode.Software // 作为后备方案
                }
            })
            .LogToTrace();

    /// <summary>
    /// 离屏渲染界面截图。窗口尺寸固定 1600x900 —— 展示的是"一个完整屏幕的界面"，
    /// 不像主题画廊那样需要按内容自适应高度，所以直接给它一个真实分辨率即可。
    /// </summary>
    private static void Capture(string outPath, string[] args)
    {
        AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia()
            .WithInterFont()
            .SetupWithoutStarting();

        // 复用与正式启动相同的组装（App.CreateMainViewModel），
        // 截图里的服务拓扑因此与真实运行时一致：预览的界面就是运行时那个界面。
        var vm = App.CreateMainViewModel(args);

        var dark = args.Contains("--dark");

        var window = new MainWindow
        {
            DataContext = vm,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };

        window.Show();

        // headless 模式下必须手动推进消息循环，否则布局与渲染都还没发生
        Dispatcher.UIThread.RunJobs();

        // 可选：展开指定弹层（菜单栏下拉本身是 Popup，静态截图里默认不存在）。
        // 用法：--open MenuFile —— 名字取自 MainWindow.axaml 的 x:Name。
        var openIndex = Array.IndexOf(args, "--open");
        if (openIndex >= 0 && openIndex + 1 < args.Length)
        {
            ExpandPopup(window, args[openIndex + 1]);
            Dispatcher.UIThread.RunJobs();
        }

        // 可选：展开 3D 视口右上角的机械臂状态浮层。
        // 它默认是【收起】的（收起时只有一枚小按钮），静态截图里拍不到，所以要显式按一下。
        // 用法：--status
        if (args.Contains("--status"))
        {
            vm.Status.IsPanelVisible = true;
            Dispatcher.UIThread.RunJobs();
        }

        // 可选：注入一帧合成点云（--demo-cloud）。没有后端可连时，这是唯一能把
        // 「/sensor 解码 → 视口邮箱 → 点云图层 → GL」这条链路画进 PNG 的办法：
        // 帧按协议的线格式真的拼了一遍、再用生产的解码器解回来，只跳过 WebSocket 传输。
        if (args.Contains("--demo-cloud"))
        {
            vm.PublishPointCloud(DemoSensorFrame.Build(seq: 1024));
            Dispatcher.UIThread.RunJobs();
        }

        var frame = window.CaptureRenderedFrame();
        if (frame is null)
        {
            Console.Error.WriteLine("截图失败：窗口未完成渲染");
            Environment.Exit(1);
            return;
        }

        frame.Save(outPath);
        Console.WriteLine(
            $"已保存 {outPath}  ({frame.PixelSize.Width}x{frame.PixelSize.Height}, " +
            $"{(dark ? "Dark" : "Light")}, {(vm.IsClinicalMode ? "临床模式" : "工程师模式")})");
    }

    /// <summary>
    /// 展开一个弹层。ComboBox 用 IsDropDownOpen，MenuItem 用 IsSubMenuOpen，
    /// 普通 Button 用 Flyout.ShowAt —— 三类控件的展开入口各不相同。
    /// </summary>
    internal static void ExpandPopup(Window window, string name)
    {
        var target = window.FindControl<Control>(name);

        // 诊断输出走 stderr：stdout 被重定向到文件时是块缓冲，
        // 进程被 kill 掉会丢掉最后一段，而"窗口模式"下正是靠 kill 结束进程的。
        Console.Error.WriteLine($"[ui] 展开 {name} → {target?.GetType().Name ?? "<未找到>"}");

        switch (target)
        {
            case ComboBox combo:
                combo.IsDropDownOpen = true;
                break;
            case MenuItem menuItem:
                menuItem.IsSubMenuOpen = true;
                break;
            case Button button when button.Flyout is { } flyout:
                flyout.ShowAt(button);
                break;
            case null:
                Console.Error.WriteLine($"找不到控件：{name}");
                break;
            default:
                Console.Error.WriteLine($"控件 {name}（{target.GetType().Name}）不是可展开的弹层宿主");
                break;
        }

        // 弹层会新建自己的视觉树，展开后至少还要再推进一次布局，否则它还是 0 尺寸
        Dispatcher.UIThread.RunJobs();
    }
}
