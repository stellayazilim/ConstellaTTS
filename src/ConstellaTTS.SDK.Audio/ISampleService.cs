using System.Collections.Specialized;

namespace ConstellaTTS.SDK.Audio;

/// <summary>
/// Read-side repository for the active project's sample catalogue.
/// Holds the live in-memory list of samples, hydrates it from the
/// filesystem when the active project changes, and accepts
/// incremental updates from importers when new samples land in the
/// project folder.
///
/// <para>
/// <b>Repository, not orchestrator.</b> The service is the source of
/// truth for "what samples does the current project have"; it does
/// not import, transcode, or mutate files itself. Importers (the
/// upload action today, drag-drop / clipboard / IPC paths later)
/// drop WAVs into the project's
/// <see cref="App.IConstellaProject.SamplesPath"/> directory and
/// then notify the service through whatever event hooks the
/// composition layer wires in. The service folds the new entries
/// into <see cref="Samples"/>; nobody in this layer talks to the
/// processor or the file system on behalf of an importer.
/// </para>
///
/// <para>
/// <b>Catalogue scope.</b> Every project's own
/// <see cref="App.IConstellaProject.SamplesPath"/> is always
/// scanned. On top of that, every entry in the project's
/// <see cref="App.IConstellaProject.SampleLibraries"/> is treated
/// as a path to another project's <c>project.ctts</c>; that
/// project's <c>samples/</c> directory is scanned and its samples
/// are merged into the same catalogue. The merge is flat —
/// referenced libraries' own <c>SampleLibraries</c> are <i>not</i>
/// followed transitively, so the user gets exactly what they asked
/// for and no surprises.
/// </para>
///
/// <para>
/// <b>Hybrid refresh model.</b> When the active project changes the
/// service does a full scan of the project's own samples directory
/// plus every referenced library's. Between project switches the
/// service is event-driven: importers report their freshly written
/// paths through <see cref="OnSamplesAdded"/> and the service
/// appends them without re-walking the directories. A user-driven
/// rescan is available through <see cref="Refresh"/> for cases
/// where files have been added or removed out-of-band (drag-and-
/// drop into the folder via the file manager, an external tool
/// writing into <c>samples/</c>); orphan detection happens during
/// that pass.
/// </para>
///
/// <para>
/// <b>Why both the collection signal and dedicated events.</b>
/// <see cref="INotifyCollectionChanged"/> is what XAML
/// <c>ItemsControl</c> bindings consume; subscribers that want a
/// strongly-typed payload (a sample-removal confirmation dialog, a
/// logger that records every import) shouldn't have to unpack
/// <see cref="NotifyCollectionChangedEventArgs"/>. The dedicated
/// events forward the same transitions in
/// <see cref="SampleEventArgs"/> form for those cases.
/// </para>
/// </summary>
public interface ISampleService
{
    /// <summary>
    /// The samples currently known to the active project — the
    /// project's own <c>samples/</c> directory plus every
    /// referenced library's. Empty when no project is active.
    /// Implementations expose a live collection that raises
    /// <see cref="INotifyCollectionChanged"/> on add / remove so
    /// XAML <c>ItemsControl</c> bindings refresh without
    /// code-behind.
    /// </summary>
    IReadOnlyList<Sample> Samples { get; }

    /// <summary>Raised after a sample has been added to the catalogue.</summary>
    event EventHandler<SampleEventArgs>? SampleAdded;

    /// <summary>Raised after a sample has been removed from the catalogue.</summary>
    event EventHandler<SampleEventArgs>? SampleRemoved;

    /// <summary>
    /// Full rescan of every directory the active project's
    /// catalogue covers — its own <c>samples/</c> and each
    /// referenced library's. Drops the in-memory list and
    /// rebuilds from disk; the right call after an out-of-band
    /// filesystem change (the user dropped a WAV into the folder
    /// directly, an external tool wrote some, etc.). Also runs
    /// automatically on project switch.
    /// </summary>
    void Refresh();

    /// <summary>
    /// Fold one or more freshly written sample paths into
    /// <see cref="Samples"/>. The composition layer wires this to
    /// the upload action's completion signal so importers don't
    /// have to know about the service. Files are expected to
    /// already exist on disk — the service stats each one to read
    /// its format facts and skips silently if the path is missing
    /// (which only happens if a write race or out-of-band delete
    /// beat the notification, both of which the next
    /// <see cref="Refresh"/> would resolve anyway).
    /// </summary>
    void OnSamplesAdded(IReadOnlyList<string> paths);
}
