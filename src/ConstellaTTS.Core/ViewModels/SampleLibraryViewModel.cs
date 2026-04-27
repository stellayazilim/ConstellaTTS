using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ConstellaTTS.Core.Actions;
using ConstellaTTS.Core.Windows;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.Audio;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.UI.Actions;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// Backs the sample library panel. Four roles:
///
/// <list type="bullet">
///   <item><b>Importer wiring.</b> Holds <see cref="SampleUploadAction"/>
///   so the file-picker / drag-drop / clipboard paths drive a
///   single command surface, and the service can subscribe to its
///   <see cref="ISampleUploadAction.SamplesUploaded"/> event for
///   incremental catalogue updates.</item>
///   <item><b>Catalogue read.</b> Exposes
///   <see cref="ISampleService.Samples"/> as <see cref="Samples"/>
///   for the view's <c>ItemsControl</c> binding. The service owns
///   the live collection; the view-model is a thin pass-through.</item>
///   <item><b>Microphone recording.</b> Owns the start/stop toggle,
///   tracks <see cref="IsRecording"/> for the button visual, and
///   on stop hands the freshly written WAV through a name-prompt
///   modal before letting it land in the project's catalogue.</item>
///   <item><b>User feedback.</b> Surfaces import / record progress
///   through <see cref="StatusText"/>, fed both from awaited paths
///   and from the action's <c>AsyncExecutionCompleted</c> event.</item>
/// </list>
///
/// <para>
/// <b>Why no save trigger after import / recording.</b> The project
/// manifest doesn't track individual samples — the filesystem is
/// the catalogue. New WAVs lands in <c>samples/</c>, the service
/// folds them in through the action's event (or the recording
/// flow's direct call); nothing in the manifest needs to change,
/// so nothing is saved. <see cref="IProjectManager.SaveAsync"/> is
/// reserved for changes that <i>do</i> live in the manifest
/// (library references, future user-edited fields).
/// </para>
///
/// <para>
/// <b>Recording lifecycle.</b> Tap the record button → recorder
/// streams to <c>&lt;projectRoot&gt;/tmp/recording_&lt;timestamp&gt;.wav</c>
/// while <see cref="IsRecording"/> is true. Tap again →
/// recorder stops, the tmp file finalizes, and the
/// <see cref="SampleNameDialog"/> opens. On <b>Save</b> the tmp
/// file is renamed (with a numeric suffix if it would collide)
/// into <c>samples/</c> and folded into the catalogue; on
/// <b>Cancel</b> the tmp file is deleted. Living under the
/// project's own <c>tmp/</c> instead of the OS temp directory
/// keeps the user's system clean — abandoned recordings disappear
/// when the project does, and startup-time tmp cleanup is a
/// per-project concern.
/// </para>
/// </summary>
public sealed partial class SampleLibraryViewModel : ObservableObject
{
    private readonly SampleUploadAction  _uploadAction;
    private readonly ISampleService      _sampleService;
    private readonly IMicrophoneRecorder _recorder;
    private readonly IProjectManager     _projectManager;

    /// <summary>
    /// Live sample catalogue for the active project. Pass-through to
    /// <see cref="ISampleService.Samples"/>; the service raises
    /// <see cref="System.Collections.Specialized.INotifyCollectionChanged"/>
    /// on add / remove so XAML <c>ItemsControl</c> bindings refresh
    /// without code-behind.
    /// </summary>
    public IReadOnlyList<Sample> Samples => _sampleService.Samples;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// True between a successful start and the matching stop. Bound
    /// to the record button's visual state — XAML toggles between
    /// hollow circle (idle) and filled red dot (live) through a
    /// trigger on this property.
    /// </summary>
    [ObservableProperty]
    private bool _isRecording;

    /// <summary>
    /// The path the recorder is currently writing to, or null when
    /// idle. Tracked separately from <see cref="IsRecording"/> so
    /// stop logic doesn't have to reach into the recorder for it
    /// (and the recorder gets to clear its own state cleanly inside
    /// StopAsync).
    /// </summary>
    private string? _activeRecordingPath;

    public SampleLibraryViewModel(
        SampleUploadAction  uploadAction,
        ISampleService      sampleService,
        IMicrophoneRecorder recorder,
        IProjectManager     projectManager)
    {
        _uploadAction   = uploadAction;
        _sampleService  = sampleService;
        _recorder       = recorder;
        _projectManager = projectManager;

        // Wire action → service. The action publishes the on-disk
        // paths it just wrote; the service folds them into its
        // catalogue. Subscribed once here, in the constructor —
        // attaching inside Execute or per-import would leak handlers
        // and produce duplicate updates.
        _uploadAction.SamplesUploaded += OnSamplesUploaded;

        // Status feedback for the ICommand / keybind dispatch path.
        // The awaited ImportPathsAsync below sets StatusText directly;
        // this handler only matters when the sync ICommand entry
        // point fires-and-forgets, where the event is the only
        // completion signal subscribers see.
        _uploadAction.AsyncExecutionCompleted += OnUploadCompleted;
    }

    private void OnSamplesUploaded(object? sender, SamplesUploadedEventArgs e)
    {
        _sampleService.OnSamplesAdded(e.OutputPaths);
    }

    private void OnUploadCompleted(object? sender, AsyncActionResult result)
    {
        StatusText = result.Success
            ? "Sample yüklendi."
            : $"Hata: {result.Error?.Message ?? "bilinmiyor"}";
    }

    /// <summary>
    /// Single entry point for all import sources (file dialog,
    /// drag-drop, clipboard paste). Awaits the upload action so the
    /// caller's UI can show in-flight state directly; the
    /// <c>SamplesUploaded</c> event still fires for the service
    /// regardless of how the action was started.
    /// </summary>
    private async Task ImportPathsAsync(IReadOnlyList<string>? paths)
    {
        if (paths is null || paths.Count == 0) return;

        StatusText = $"Yükleniyor: {paths.Count} dosya…";
        try
        {
            await _uploadAction.ExecuteAsync(paths);
            StatusText = $"Tamamlandı: {paths.Count} sample.";
        }
        catch (Exception ex)
        {
            StatusText = $"Hata: {ex.Message}";
        }
    }

    [RelayCommand]
    public async Task UploadSample(object sender)
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
        // surface in samples/.
        var paths = selection
            .Select(f => f.TryGetLocalPath())
            .Where(p => p is not null)
            .Select(p => p!)
            .ToList();

        if (paths.Count == 0) return;

        await ImportPathsAsync(paths);
    }

    /// <summary>
    /// Manual rescan trigger for the panel's "refresh" button.
    /// Picks up samples that landed in the folder out-of-band —
    /// dragged in via the file manager, written by an external
    /// tool, recovered from a backup. Cheap enough to run on demand;
    /// no progress UI needed today.
    /// </summary>
    [RelayCommand]
    public void Refresh()
    {
        _sampleService.Refresh();
        StatusText = $"Liste yenilendi: {Samples.Count} sample.";
    }

    /// <summary>
    /// One command, two transitions: when idle, opens the recorder
    /// and starts streaming PCM into a tmp file; when live, stops
    /// the recorder and steers the result through the name-prompt
    /// modal. Single command rather than two keeps the XAML simple
    /// (one <c>Command</c> binding on the record button) and
    /// matches the actual user gesture — they tap the same button
    /// to start and to stop.
    /// </summary>
    [RelayCommand]
    public async Task ToggleRecording(object sender)
    {
        if (sender is not Visual visual) return;

        var owner = TopLevel.GetTopLevel(visual) as Window;
        if (owner is null) return;

        if (!IsRecording)
            await StartRecordingAsync();
        else
            await StopAndPromptAsync(owner);
    }

    private async Task StartRecordingAsync()
    {
        var project = _projectManager.Active;
        if (project is null)
        {
            StatusText = "Aktif proje yok; kayıt yapılamaz.";
            return;
        }

        // Filename includes the timestamp so a session that records
        // many takes back-to-back never collides with itself even
        // before the user names them. The tmp directory itself is
        // created lazily here rather than at project open — keeps
        // the layout work scoped to the operations that actually
        // need it.
        var tmpDir   = project.TmpPath;
        Directory.CreateDirectory(tmpDir);

        var filename = $"recording_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.wav";
        var path     = Path.Combine(tmpDir, filename);

        try
        {
            await _recorder.StartAsync(path);
            _activeRecordingPath = path;
            IsRecording          = true;
            StatusText           = "Kayıt başladı…";
        }
        catch (Exception ex)
        {
            StatusText = $"Kayıt başlatılamadı: {ex.Message}";
        }
    }

    private async Task StopAndPromptAsync(Window owner)
    {
        string tmpPath;
        try
        {
            tmpPath = await _recorder.StopAsync();
        }
        catch (Exception ex)
        {
            StatusText = $"Kayıt durdurulamadı: {ex.Message}";
            IsRecording = false;
            _activeRecordingPath = null;
            return;
        }

        IsRecording          = false;
        _activeRecordingPath = null;
        StatusText           = "Kayıt tamamlandı.";

        var project = _projectManager.Active;
        if (project is null)
        {
            // No active project to land into — abandoned recording
            // gets cleaned up rather than orphaned in tmp.
            TryDelete(tmpPath);
            StatusText = "Aktif proje yok; kayıt iptal edildi.";
            return;
        }

        // Default name = the tmp file's stem, so the user just hits
        // Enter to keep the timestamped name or types a meaningful
        // one to overwrite it.
        var defaultName = Path.GetFileNameWithoutExtension(tmpPath);
        var chosenName  = await SampleNameDialog.ShowAsync(owner, defaultName);

        if (string.IsNullOrEmpty(chosenName))
        {
            // Cancel — discard the tmp recording. The user explicitly
            // chose not to keep it, so we don't leave it lying around
            // in the project's tmp folder.
            TryDelete(tmpPath);
            StatusText = "Kayıt iptal edildi.";
            return;
        }

        Directory.CreateDirectory(project.SamplesPath);
        var finalPath = ResolveCollisionFreePath(project.SamplesPath, chosenName);

        try
        {
            File.Move(tmpPath, finalPath);
        }
        catch (Exception ex)
        {
            StatusText = $"Kayıt kaydedilemedi: {ex.Message}";
            TryDelete(tmpPath);
            return;
        }

        // Fold the freshly named file into the catalogue. Same
        // incremental-update path the upload action uses — the
        // service stats the file for its header and appends it to
        // the live collection.
        _sampleService.OnSamplesAdded(new[] { finalPath });
        StatusText = $"Kayıt eklendi: {Path.GetFileName(finalPath)}";
    }

    /// <summary>
    /// Picks a non-colliding path under <paramref name="samplesDir"/>
    /// for the user-typed name. Tries the bare name first, then
    /// <c>name (2).wav</c>, <c>name (3).wav</c>, …  — same scheme the
    /// upload action uses, so collision behaviour is consistent
    /// across import routes.
    /// </summary>
    private static string ResolveCollisionFreePath(string samplesDir, string stem)
    {
        var direct = Path.Combine(samplesDir, $"{stem}.wav");
        if (!File.Exists(direct)) return direct;

        for (int i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(samplesDir, $"{stem} ({i}).wav");
            if (!File.Exists(candidate)) return candidate;
        }

        // Pathological state — the directory is full of name (2)…
        // (999) collisions. Falling back to a guid keeps the
        // recording from being lost; the user can rename later.
        return Path.Combine(samplesDir, $"{stem}_{Guid.NewGuid():N}.wav");
    }

    /// <summary>
    /// Best-effort delete of an abandoned tmp recording. Logging
    /// will pick up failures once ILogger is wired into this layer;
    /// for now, a stuck file in tmp/ is harmless because startup
    /// cleanup will sweep it up next session.
    /// </summary>
    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Swallow — the file may be locked by an antivirus that
            // hasn't released its scan handle yet, or by an OS-level
            // delay after StopRecording. The next project-open tmp
            // cleanup will handle it.
        }
    }
}
