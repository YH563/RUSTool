using Avalonia.Controls;

namespace RUSTool.Views.Debug;

public partial class DebugWorkspace : UserControl
{
    public DebugWorkspace()
    {
        InitializeComponent();
        ClearLogButton.Click += (_, _) => LogViewer.Clear();
        LogViewer.Log("调试工作区已加载");
    }
}
