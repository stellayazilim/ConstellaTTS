using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConstellaTTS.Core.Actions;
using ConstellaTTS.SDK.IO;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// Backs the sample library panel. For now the panel is a thin pipe: the
/// view collects file paths from the dialog/drop/paste sources, the
/// view-model funnels them through a <see cref="FileUploadAction"/> which
/// copies each file into the application tmp directory. No in-memory
/// catalog is kept here yet — the on-disk tmp folder is the source of
/// truth for this test slice. A proper sample-list view-model arrives
/// with the sample-manager step.
///
/// <para>
/// <b>Command surface.</b> <see cref="ImportPathsCommand"/> is generated
/// from <see cref="ImportPathsAsync"/> by the CommunityToolkit source
/// generator. It accepts an <see cref="IEnumerable{T}"/> of source paths,
/// so the same command serves the file-dialog, drag-drop and clipboard
/// paste paths once those wire into the view via Behaviors/Interactions.
/// </para>
/// </summary>
public sealed partial class SampleLibraryViewModel : ObservableObject
{
    private readonly IFileWriter _fileWriter;
    private readonly string      _tmpRoot;

    [ObservableProperty]
    private string _searchText = string.Empty;

    public SampleLibraryViewModel(IFileWriter fileWriter)
    {
        _fileWriter = fileWriter;

        // Walk up from bin/Debug/<tfm>/ to the project's Core directory,
        // then up two more to the repo root, and place tmp/ alongside
        // the .slnx. Quick-and-dirty for now — a path-resolution
        // service will own this once the audio runtime needs the same
        // location.
        _tmpRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tmp"));
    }

    /// <summary>
    /// Single entry point for all import sources (file dialog, drag-drop,
    /// clipboard paste). Each call constructs a fresh
    /// <see cref="FileUploadAction"/>, executes it, and lets it copy the
    /// files into tmp. History wiring will be added once the panel has
    /// a visible list to undo against.
    /// </summary>
    [RelayCommand]
    private Task ImportPathsAsync(IEnumerable<string>? paths)
    {
        if (paths is null) return Task.CompletedTask;

        var pathList = paths as IReadOnlyList<string> ?? paths.ToList();
        if (pathList.Count == 0) return Task.CompletedTask;

        // The action's Execute is synchronous (it blocks on the underlying
        // IFileWriter via GetAwaiter().GetResult). Wrapping the call in
        // Task.Run keeps the UI thread responsive while the copy runs;
        // for the small files this panel handles today the difference is
        // imperceptible, but it keeps the command honest about being
        // async-shaped at the contract level.
        return Task.Run(() =>
        {
            var action = new FileUploadAction(pathList, _fileWriter, _tmpRoot);
            action.Execute();
        });
    }

}
