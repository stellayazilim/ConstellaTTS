using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Rename a track in place. Self-symmetric — its
/// <see cref="Reverse"/> returns a fresh
/// <see cref="RenameTrackAction"/> with the names swapped, so
/// undo / redo of a rename walks the user back and forward through
/// the same two values.
///
/// <para>
/// <b>Caller contract.</b> The header's TextBox is two-way bound
/// to <see cref="ITrackViewModel.Name"/>, so by the time the
/// caller dispatches this action the VM already holds the new
/// name — typing into the box pushed each character through. The
/// action's <see cref="Execute"/> is idempotent against that
/// (re-assigning the same value is a no-op for the binding) and
/// exists to enforce the post-rename invariant: after Execute,
/// VM and persist-side both carry <see cref="_newName"/>.
/// </para>
///
/// <para>
/// <b>Collision policy is the project's.</b> If the new name
/// matches a different track,
/// <see cref="IConstellaProject.RenameTrack"/> is documented to
/// no-op rather than merge. The VM has no analogous defence
/// (TextBox binding accepts whatever the user typed), so the
/// project-side and VM-side names can desynchronise on collision.
/// That's accepted — names are user-typed identifiers and the
/// next save reflects whatever the VM currently holds; a future
/// caller-side validation can pre-check before dispatching.
/// </para>
///
/// <para>
/// Self-flushing — <see cref="IProjectManager.SaveAsync"/> is
/// called inside <see cref="Persist"/>.
/// </para>
/// </summary>
public sealed class RenameTrackAction : ActionBase, IReversible, IPersistable
{
    private readonly ITrackViewModel _track;
    private readonly string          _oldName;
    private readonly string          _newName;

    public override string  Id          => "RenameTrackAction";
    public override string  Name        => "Track Yeniden Adlandır";
    public override string? Description => $"'{_oldName}' → '{_newName}'.";

    public RenameTrackAction(ITrackViewModel track, string oldName, string newName)
    {
        _track   = track;
        _oldName = oldName;
        _newName = newName;
    }

    public override void Execute(object? data = null)
    {
        // Idempotent against the two-way binding that already pushed
        // the new value into the VM. Explicit assignment exists so
        // an action invoked WITHOUT a TextBox path (e.g. a future
        // command-palette rename, a script-driven test) still ends
        // up with the VM holding the new name.
        _track.Name = _newName;
    }

    public async Task Persist(IProjectManager manager)
    {
        var project = manager.Active;
        if (project is null) return;

        // Project-side rename uses the OLD name as the lookup key
        // (it's still the on-disk identity until this call) and
        // installs the new one. RenameTrack is documented to no-op
        // on collision — see the class-level remarks for what that
        // means for VM/persist consistency.
        project.RenameTrack(_oldName, _newName);

        await manager.SaveAsync();
    }

    /// <inheritdoc />
    public IAction Reverse(IReversible? previous, object? data = null) =>
        new RenameTrackAction(_track, _newName, _oldName);
}
