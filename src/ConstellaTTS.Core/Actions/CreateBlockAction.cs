
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Adds a specific block to a specific track and resolves any collisions
/// by bumping colliding sections to the right (see <see cref="BlockBumping"/>).
/// Holds concrete VM references captured at construction time; also captures
/// the bumps applied by its Execute so the inverse <see cref="RemoveBlockAction"/>
/// can restore original positions on undo.
///
/// Used from two call sites:
///   • The create gesture in TrackListView: on release, constructs the
///     action, executes it, then pushes it as the history entry.
///   • History.Rollback: when <see cref="RemoveBlockAction"/>.Reverse() is
///     called (Ctrl+Y to redo a removal), it returns an instance of this
///     action to re-add the block.
///
/// Implements <see cref="IReversible"/> — <c>Reverse()</c> returns a
/// <see cref="RemoveBlockAction"/> that carries the bump snapshot, which
/// is itself reversible, so the create⇄remove loop supports unbounded
/// undo/redo chains with consistent bumping state at every step.
/// </summary>
public sealed class CreateBlockAction : ActionBase, IReversible
{
    private readonly ITrackViewModel         _track;
    private readonly IStageViewModel         _block;
    private          IReadOnlyList<BumpRecord> _bumpsApplied = System.Array.Empty<BumpRecord>();

    public override string  Id          => "CreateBlockAction";
    public override string  Name        => "Block Oluştur";
    public override string? Description => $"{_track.Name} track'ine '{_block.Label}' bloğunu ekler.";

    public CreateBlockAction(ITrackViewModel track, IStageViewModel block)
    {
        _track = track;
        _block = block;
    }

    public override void Execute(object? data = null)
    {
        // Compute bumps BEFORE adding — the new block is passed by reference
        // so BlockBumping.Compute can reason about where it will land without
        // actually mutating the collection yet.
        _bumpsApplied = BlockBumping.Compute(_track, _block);

        // Apply the bumps first, then insert the new block. Doing bumps first
        // means the collection is in a consistent (overlap-free) state at the
        // moment the ObservableCollection.Added event fires for the new block,
        // which keeps visual state clean during binding re-evaluation.
        BlockBumping.Apply(_bumpsApplied);
        _track.Sections.Add(_block);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns a <see cref="RemoveBlockAction"/> carrying the bump snapshot
    /// captured during Execute. The caller executes it (block removed,
    /// original positions restored); the history manager may then push
    /// that returned action onto the redo stack, whose own Reverse() will
    /// produce a fresh CreateBlockAction that recomputes bumps identically
    /// — round-trippable.
    /// </remarks>
    public IAction Reverse(IReversible? previous, params object[] args) =>
        new RemoveBlockAction(_track, _block, _bumpsApplied);
}
