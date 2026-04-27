using ConstellaTTS.Core.ViewModels;
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Append a track to the timeline. Used in two shapes:
///
///   • <b>User-facing add</b> — the toolbar's "+ Track" button
///     dispatches <see cref="AddTrackAction(TrackListViewModel, string?)"/>
///     with whatever name the dialog produced (or null for a
///     placeholder). A fresh empty <see cref="TrackData"/> is
///     persisted on the on-disk side.
///
///   • <b>Undo of remove</b> —
///     <see cref="RemoveTrackAction.Reverse"/> dispatches
///     <see cref="AddTrackAction(TrackListViewModel, TrackData)"/>
///     with the snapshot the remove captured before deleting. The
///     snapshot carries the track's blocks, so the round-trip
///     restores everything the user lost when they hit Ctrl+Z, not
///     just the empty row.
///
/// <para>
/// Wraps the IPersistable+IReversible scaffolding so undo, redo, and
/// on-disk state all stay in lock step. Self-flushing — the
/// manager's <see cref="IProjectManager.SaveAsync"/> is called
/// inside <see cref="Persist"/>; callers stay on the trim three-step
/// <c>Execute → Persist → Push</c> pattern.
/// </para>
/// </summary>
public sealed class AddTrackAction : ActionBase, IReversible, IPersistable
{
    private readonly TrackListViewModel _listVm;
    private readonly string?            _customName;
    private readonly TrackData?         _snapshot;
    private readonly int                _insertAt;

    /// <summary>
    /// VM created during <see cref="Execute"/>; captured so
    /// <see cref="Reverse"/> can target it by reference. Null
    /// before Execute runs.
    /// </summary>
    private ITrackViewModel? _created;

    public override string  Id          => "AddTrackAction";
    public override string  Name        => "Track Ekle";
    public override string? Description => _snapshot is not null
        ? $"'{_snapshot.Name}' adlı track'i geri yükler."
        : string.IsNullOrWhiteSpace(_customName)
            ? "Yeni track ekler."
            : $"'{_customName}' adlı track ekler.";

    /// <summary>
    /// User-facing constructor. Adds an empty track with the given
    /// name (null/empty falls through to a "Track N" placeholder
    /// inside <see cref="TrackListViewModel.AddTrack(string?)"/>).
    /// </summary>
    public AddTrackAction(TrackListViewModel listVm, string? customName)
    {
        _listVm     = listVm;
        _customName = customName;
        _snapshot   = null;
        _insertAt   = -1; // append
    }

    /// <summary>
    /// Snapshot-restore constructor — produced only by
    /// <see cref="RemoveTrackAction.Reverse"/>. Rebuilds the track
    /// (blocks and all) from the data the remove captured before
    /// deletion, so undo restores the full row instead of an empty
    /// shell.
    ///
    /// <para>
    /// <paramref name="insertAt"/> is the original list position
    /// the track lived at before removal — the user expects Ctrl+Z
    /// to put a deleted-from-the-middle row back into its slot, not
    /// onto the end. The view-model and the project both clamp
    /// out-of-range values to the nearest valid edge, so a negative
    /// or past-the-end index degrades gracefully to an append.
    /// </para>
    /// </summary>
    public AddTrackAction(TrackListViewModel listVm, TrackData snapshot, int insertAt)
    {
        _listVm     = listVm;
        _customName = null;
        _snapshot   = snapshot;
        _insertAt   = insertAt;
    }

    public override void Execute(object? data = null)
    {
        if (_snapshot is not null)
        {
            // Restore-from-snapshot path. Build the VM directly from
            // the snapshot using TrackViewModel's hydration
            // constructor — same code the project-open path uses, so
            // a restored row is structurally indistinguishable from
            // a freshly-loaded one. The palette entry is picked at
            // the track's eventual list position so the colour
            // scheme stays deterministic.
            //
            // _insertAt may be -1 in pathological cases (caller bug,
            // or a snapshot constructed without a position); in that
            // case fall through to an append, which is the same
            // failure mode TrackListViewModel.InsertTrack would
            // produce after clamping.
            var idx     = _insertAt >= 0 ? _insertAt : _listVm.Tracks.Count;
            if (idx > _listVm.Tracks.Count) idx = _listVm.Tracks.Count;
            var palette = TrackListViewModel.PaletteAt(idx);
            var vm      = new TrackViewModel(idx, _snapshot, palette.color, palette.bg);
            _listVm.InsertTrack(idx, vm);
            _created = vm;
            return;
        }

        // Fresh-add path. Delegating to the view-model keeps the
        // palette + Id + placeholder-name logic in one place — the
        // same code path the launcher's quick-add button uses. The
        // action just grabs the result so it can address it later.
        _listVm.AddTrack(_customName);
        _created = _listVm.Tracks.Count > 0 ? _listVm.Tracks[^1] : null;
    }

    public async Task Persist(IProjectManager manager)
    {
        var project = manager.Active;
        if (project is null || _created is null) return;

        // Snapshot the VM as TrackData. For the fresh-add path the
        // VM has no blocks yet, so the result is essentially
        // (Name, Order, []). For the restore path, the snapshot
        // already carries the blocks; the VM was hydrated from it,
        // so re-snapshotting the VM gives an equivalent shape and
        // avoids a special branch here.
        var trackData = new TrackData
        {
            Name   = _created.Name,
            Order  = _created.Order,
            Blocks = _created.Sections.Select(BlockSerialization.ToData).ToList(),
        };

        if (_snapshot is not null && _insertAt >= 0)
        {
            // Undo-of-remove path: restore the track at its
            // original on-disk position so block IDs the user
            // might still be holding (selection, history entries
            // pushed AFTER this one) keep resolving the same way
            // they did before the remove.
            project.InsertTrack(_insertAt, trackData);
        }
        else
        {
            project.AddTrack(trackData);
        }

        await manager.SaveAsync();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a <see cref="RemoveTrackAction"/> targeting the
    /// freshly-created VM. The remove action will snapshot the
    /// track's state at the time of removal so its own Reverse can
    /// re-add it identically — closing the add⇄remove loop with
    /// full block fidelity.
    /// </remarks>
    public IAction Reverse(IReversible? previous, object? data = null)
    {
        if (_created is null)
            throw new InvalidOperationException(
                "AddTrackAction.Reverse called before Execute populated the created VM.");

        return new RemoveTrackAction(_listVm, _created);
    }
}
