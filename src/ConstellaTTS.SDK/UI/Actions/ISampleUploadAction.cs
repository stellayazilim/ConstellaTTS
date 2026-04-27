namespace ConstellaTTS.SDK.UI.Actions;

/// <summary>
/// Contract for the action that ingests source audio files into the
/// active project's sample directory. Lives at the SDK level
/// alongside the other action interfaces because it's a UI / command
/// surface, not an audio-domain type — the audio SDK owns the
/// pipeline (decoders, processors, the catalogue service); this
/// layer owns the commands that ride on top of them.
///
/// <para>
/// <b>Async-shaped via <see cref="IAsyncAction"/>.</b> Inherits the
/// awaitable <c>ExecuteAsync</c> so view-models that drive the
/// action directly can flow with the work; the sync ICommand entry
/// point comes from <see cref="IAction"/> for XAML / keybind
/// dispatch.
/// </para>
///
/// <para>
/// <b>Two completion signals, two questions.</b> The base
/// <c>AsyncExecutionCompleted</c> from <see cref="AsyncActionBase"/>
/// answers "did the work succeed"; <see cref="SamplesUploaded"/>
/// here answers "which files now exist on disk". Subscribers
/// usually care about exactly one — a status bar wants success,
/// the sample service wants the path list — and forcing them onto
/// the same channel would mean every consumer filters and unpacks.
/// </para>
/// </summary>
public interface ISampleUploadAction : IAsyncAction
{
    /// <summary>
    /// Raised once at the end of a successful batch import, carrying
    /// every output path the action just wrote. Not raised on
    /// failure — partial-batch reporting (some files succeeded, some
    /// didn't) is a nice-to-have that can land later by changing
    /// this contract; for now success is all-or-nothing on the batch.
    /// </summary>
    event EventHandler<SamplesUploadedEventArgs>? SamplesUploaded;
}

/// <summary>
/// Carries the on-disk paths of samples an importer has just written
/// into the active project's sample directory. Subscribers fold
/// them into their state without re-scanning the directory.
/// </summary>
public sealed record SamplesUploadedEventArgs(IReadOnlyList<string> OutputPaths);
