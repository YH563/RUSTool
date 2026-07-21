using System;
using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Threading;

namespace RUSTool.Views.Debug;

public partial class LogView : UserControl
{
    public ObservableCollection<string> LogEntries { get; } = new();

    public LogView()
    {
        InitializeComponent();
        LogListBox.ItemsSource = LogEntries;
        Log("日志系统已启动");
    }

    public void Log(string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
            LogEntries.Add($"[{timestamp}] {message}");
            if (LogEntries.Count > 500)
                LogEntries.RemoveAt(0);
            LogListBox.ScrollIntoView(LogEntries[^1]);
        });
    }
}
