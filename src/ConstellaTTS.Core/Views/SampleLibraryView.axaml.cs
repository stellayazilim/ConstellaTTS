using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ConstellaTTS.Core.ViewModels;

namespace ConstellaTTS.Core.Views;

public partial class SampleLibraryView : UserControl
{
    public SampleLibraryView(SampleLibraryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    /// <summary>
    /// Opens the system file picker, resolves the user's selection to
    /// local filesystem paths, and forwards them to
    /// <see cref="SampleLibraryViewModel.ImportPathsCommand"/>. The
    /// <see cref="TopLevel"/> needed by the storage provider is pulled
    /// off the click sender so the view itself stays handler-only —
    /// no UI-tree fields cached on the class.
    /// </summary>
    private async void OnUploadClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Visual visual) return;

        var topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel is null) return;

        var selection = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title         = "Sample dosyaları seç",
            AllowMultiple = true,
        });

        if (selection.Count == 0) return;

        // IStorageFile.TryGetLocalPath() returns null for non-local
        // sources (cloud, MTP, archive contents); those are silently
        // skipped — the user only sees what was actually copyable
        // surface in tmp.
        var paths = selection
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        if (paths.Count == 0) return;

        if (DataContext is SampleLibraryViewModel vm)
            await vm.ImportPathsCommand.ExecuteAsync(paths);
    }
}
