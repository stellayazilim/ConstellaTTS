
using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Projects;
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
///     action, executes it, persists it, then pushes it as the history
///     entry.
///   • History.Rollback: when <see cref="RemoveBlockAction"/>.Reverse() is
///     called (Ctrl+Y to redo a removal), it returns an instance of this
///     action to re-add the block.
///
/// Implements <see cref="IReversible"/> — <c>Reverse()</c> returns a
/// <see cref="RemoveBlockAction"/> that carries the bump snapshot, which
/// is itself reversible, so the create⇄remove loop supports unbounded
/// undo/redo chains with consistent bumping state at every step.
///
/// <para>
/// <b>Persist semantics.</b> <see cref="Persist"/> mirrors what
/// <see cref="Execute"/> did to the view-model into the persisted
/// project: every bumped existing block is updated in place
/// (only its <see cref="BlockData.StartSec"/> changes, since the
/// bump only moves blocks rightward), then the freshly-created
/// block is appended to <see cref="TrackData.Blocks"/>. The append
/// position is what determines the new block's ID
/// (<c>{trackName}/{blocks.Count - 1}</c>), and that ID matches the
/// block's index in the VM's <see cref="ITrackViewModel.Sections"/>
/// collection because both lists grow in the same order — the index
/// contract that <see cref="ViewModels.TrackViewModel"/>'s
/// hydration constructor preserves.
/// </para>
///
/// <para>
/// Self-flushing: after the project is mutated,
/// <see cref="IProjectManager.SaveAsync"/> is called from inside
/// <see cref="Persist"/>. The caller's three-step pattern stays
/// trim — <c>Execute → Persist → Push</c> — and a forgotten flush
/// in any one call site can't leak unsaved state.
/// </para>
/// </summary>
public sealed class CreateBlockAction : ActionBase, IReversible, IPersistable
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
    public async Task Persist(IProjectManager manager)
    {
        var project = manager.Active;
        if (project is null) return;

        // Bumped blocks moved rightward in the VM during Execute; mirror
        // that into the project. Each bump's section is still in the
        // VM's Sections collection at its original index (bumping
        // changes StartSec only, never collection position), so
        // IndexOf gives the same value the persisted blocks list uses.
        // UpdateBlock with a fresh snapshot is the simplest way to
        // capture the new StartSec without inventing a per-field
        // mutator on IConstellaProject.
        foreach (var bump in _bumpsApplied)
        {
            var idx = _track.Sections.IndexOf(bump.Section);
            if (idx < 0) continue; // defensive: block somehow no longer in VM
            var blockId = $"{_track.Name}/{idx}";
            project.UpdateBlock(blockId, BlockSerialization.ToData(bump.Section));
        }

        // Then append the newly-created block. AddBlock places it at
        // the end of the track's persisted blocks list, which lines
        // up with the index Execute landed on in the VM (also an
        // append) — so the next Persist call against this same
        // block can derive its ID from Sections.IndexOf without
        // disagreeing with the on-disk position.
        project.AddBlock(_track.Name, BlockSerialization.ToData(_block));

        await manager.SaveAsync();
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
    public IAction Reverse(IReversible? previous, object? data = null) =>
        new RemoveBlockAction(_track, _block, _bumpsApplied);
}
