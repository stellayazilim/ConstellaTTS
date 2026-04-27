using ConstellaTTS.SDK.App;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.Actions;

/// <summary>
/// One-way conversion from a live <see cref="IStageViewModel"/> /
/// <see cref="ISectionViewModel"/> to its persisted <see cref="BlockData"/>
/// shape. Used by every <see cref="ConstellaTTS.SDK.UI.Actions.IPersistable"/>
/// action that needs to write a block-shaped change into the project
/// — create, move, resize, field edit.
///
/// <para>
/// <b>One-way only.</b> Hydration goes the other direction (manifest
/// → <c>TrackData</c>/<c>BlockData</c> → view-model rebuild) and is
/// owned by <see cref="ViewModels.TrackViewModel"/>'s data
/// constructor. There is no <c>FromData</c> helper here because that
/// would invite call sites to hydrate ad hoc, breaking the single
/// rebuild path the rest of the system relies on.
/// </para>
///
/// <para>
/// <b>Why a helper, not a method on the VM.</b> Putting
/// <c>ToBlockData()</c> on the VM contract would push a
/// persistence-layer concern into a binding-shaped contract every
/// XAML data template depends on. The action layer is already the
/// place that bridges VM and project; the helper sits with the
/// actions because that's where it's called from.
/// </para>
/// </summary>
internal static class BlockSerialization
{
    /// <summary>
    /// Snapshot the live block as a <see cref="BlockData"/> POCO. The
    /// returned instance is fresh — call sites that need to keep a
    /// before/after pair simply call this twice. Geometry and label
    /// are taken from <see cref="IStageViewModel"/>; section-only
    /// fields are populated when (and only when) the block is also an
    /// <see cref="ISectionViewModel"/>, mirroring the discriminator
    /// rule on <see cref="BlockData.Kind"/>.
    ///
    /// <para>
    /// <b>Dirty is intentionally not snapshotted.</b> The flag is a
    /// runtime-only "needs regenerate" hint that doesn't survive a
    /// session boundary; storing it would only mislead the next
    /// open. See <see cref="BlockData"/>'s class-level remarks for
    /// the long version.
    /// </para>
    /// </summary>
    public static BlockData ToData(IStageViewModel block)
    {
        if (block is ISectionViewModel section)
        {
            return new BlockData
            {
                Kind           = BlockKind.Section,
                Label          = section.Label,
                StartSec       = section.StartSec,
                DurationSec    = section.DurationSec,

                Emotion        = section.Emotion,
                Temperature    = section.Temperature,
                Seed           = section.Seed,
                SeedMode       = section.SeedMode,
                EngineId       = section.EngineId,
                VoiceSampleRef = section.VoiceSampleRef,
            };
        }

        return new BlockData
        {
            Kind        = BlockKind.Stage,
            Label       = block.Label,
            StartSec    = block.StartSec,
            DurationSec = block.DurationSec,
        };
    }
}
