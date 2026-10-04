using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using RUSTool.UI.ViewModels;
using System.Threading.Tasks;

namespace RUSTool.UI.Views.Debug;

public partial class DebugWorkspace : UserControl
{
    public DebugWorkspace() => InitializeComponent();

    /// <summary>「打开…」：选一个 <c>.rusrec</c> 本地载入（回放是本地行为，不经后端）。</summary>
    private async void OnReplayOpen(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        var files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开回放录音",
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("RUS 录音") { Patterns = ["*.rusrec"] }],
        });

        if (files.Count == 0)
            return;

        var path = files[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
            await vm.Replay.LoadFileAsync(path);
    }

    /// <summary>「选择目录」：挑录音目录（与后端 recorder 的 output_dir 同一绝对目录），写入设置并刷新清单。</summary>
    private async void OnReplayPickDirectory(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm)
            return;

        var top = TopLevel.GetTopLevel(this);
        if (top is null)
            return;

        var folders = await top.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "选择录音目录（与后端 recorder 的 output_dir 相同）",
            AllowMultiple = false,
        });

        if (folders.Count == 0)
            return;

        var path = folders[0].TryGetLocalPath();
        if (string.IsNullOrEmpty(path))
            return;

        vm.Settings.RecordsDirectory = path;
        vm.Replay.RefreshListCommand.Execute(null);
    }
}
