using Avalonia.Controls;

namespace RUSTool.Views.Shell;

public partial class DebugShell : Window
{
    public DebugShell()
    {
        InitializeComponent();
        ClearLogButton.Click += (_, _) => LogViewer.LogEntries.Clear();
        LogViewer.Log("调试窗口已加载");
    }
}
