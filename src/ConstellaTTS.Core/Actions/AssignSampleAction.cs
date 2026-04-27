using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Projects;
using ConstellaTTS.SDK.UI.Actions;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// Assigns (or clears) the voice sample reference on a section. Fired
/// by the timeline's drag-drop pipeline when a sample is dropped from
/// the Sample Library window onto a section block.
///
/// <para>
/// <b>Symmetric reverse.</b> The action stores the previous and new
/// sample refs at construction; <see cref="Reverse"/> hands back a
/// fresh action with those swapped. Undo of an undo lands back at the
/// original assignment, same pattern as <c>RenameTrackAction</c> and
/// <c>ReorderTracksAction</c>.
/// </para>
///
/// <para>
/// <b>Section-only.</b> The constructor takes an
/// <see cref="IStageViewModel"/> for type-system uniformity with the
/// rest of the action layer, but it down-casts to
/// <see cref="ISectionViewModel"/> at execute time. Drop-target
/// validation in the view rejects stages, so this should never see a
/// non-section in practice; if it does, the action quietly no-ops
/// rather than throwing \u2014 a stray drop on a stage shouldn't crash
/// the editor.
/// </para>
///
/// <para>
/// <b>Persistence.</b> The new ref is mirrored into the manifest via
/// <see cref="IConstellaProject.UpdateBlock"/>. The block's index on
/// its track is read at persist time \u2014 sample assignment doesn't
/// move the block, so the index can't have shifted between Execute
/// and Persist. Self-flushing through <see cref="IProjectManager.SaveAsync"/>.
/// </para>
///
/// <para>
/// <b>Dirty flag.</b> Assigning (or clearing) the sample flips the
/// section's Dirty flag to true \u2014 any change to the engine inputs
/// invalidates the previously-rendered audio. Undo restores the
/// previous ref but leaves Dirty true; the audio cache is keyed off
/// the inputs, not off the action stack, so even a "back to where we
/// started" undo doesn't make a stale render fresh again.
/// </para>
/// </summary>
public sealed class AssignSampleAction : ActionBase, IReversible, IPersistable
{
    private readonly ITrackViewModel _track;
    private readonly IStageViewModel _block;
    private readonly string?         _oldRef;
    private readonly string?         _newRef;

    public override string  Id          => "AssignSampleAction";
    public override string  Name        => string.IsNullOrEmpty(_newRef)
        ? "Sample Kaldır"
        : "Sample Ata";
    public override string? Description =>
        $"'{_block.Label}' bloğuna sample ataması: '{_oldRef ?? "yok"}' \u2192 '{_newRef ?? "yok"}'.";

    public AssignSampleAction(
        ITrackViewModel track,
        IStageViewModel block,
        string?         oldRef,
        string?         newRef)
    {
        _track  = track;
        _block  = block;
        _oldRef = oldRef;
        _newRef = newRef;
    }

    public override void Execute(object? data = null)
    {
        if (_block is not ISectionViewModel section) return;

        section.VoiceSampleRef = _newRef;
        section.Dirty          = true;
    }

    public async Task Persist(IProjectManager manager)
    {
        var project = manager.Active;
        if (project is null) return;
        if (_block is not ISectionViewModel) return;

        // Sample assignment doesn't relocate the block, so the
        // index it had at Execute is the index it has now \u2014 no
        // stale-index risk that the move/remove actions have to
        // worry about.
        var idx = _track.Sections.IndexOf(_block);
        if (idx < 0) return;

        var blockId = $"{_track.Name}/{idx}";
        project.UpdateBlock(blockId, BlockSerialization.ToData(_block));

        await manager.SaveAsync();
    }

    /// <inheritdoc />
    public IAction Reverse(IReversible? previous, object? data = null) =>
        new AssignSampleAction(_track, _block, _newRef, _oldRef);
}
