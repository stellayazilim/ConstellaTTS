using ConstellaTTS.SDK.ViewModelContracts;
using MessagePack;

namespace ConstellaTTS.SDK.App;

/// <summary>
/// On-disk shape of a timeline block — both stages (annotations) and
/// sections (TTS-generating) in a single flat POCO. The
/// <see cref="Kind"/> discriminator says which it is; the section-only
/// fields are nullable and populated only when <see cref="Kind"/> is
/// <see cref="BlockKind.Section"/>.
///
/// <para>
/// <b>Single class, not a hierarchy.</b> See <see cref="BlockKind"/>
/// for the rationale. Briefly: keeping every block kind in one POCO
/// removes the need for MessagePack's <c>[Union]</c> registration and
/// keeps schema evolution to "add a new <see cref="KeyAttribute"/>",
/// which is the same rule the rest of the manifest follows.
/// </para>
///
/// <para>
/// <b>Colours are not persisted.</b> Block background and accent
/// colour come from the owning track's runtime palette assignment;
/// re-tinting the project (rotating palettes, importing into a host
/// with a different theme) shouldn't fight stored values. View-model
/// hidration computes colours from the track on rebuild.
/// </para>
///
/// <para>
/// <b>Dirty is not persisted.</b> The flag means "section's parameters
/// have changed since the last successful generation, so it needs to
/// re-render". A freshly opened project can't possibly know whether
/// a stored render is up-to-date with stored parameters, so the safe
/// default at hidration time is <c>Dirty = true</c> for every section
/// — the first generation reconciles. Storing the flag would only
/// mislead.
/// </para>
///
/// <para>
/// <b>Section-only fields are nullable.</b> A <see cref="BlockKind.Stage"/>
/// row leaves them null on disk; reading code branches on
/// <see cref="Kind"/> before touching them. This is cheaper than the
/// alternative (sentinel defaults like <c>Emotion = -1</c>) because
/// the discriminator is already there for branching, and a real null
/// is harder to misread than a magic number.
/// </para>
/// </summary>
[MessagePackObject]
public sealed class BlockData
{
    /// <summary>
    /// Discriminator: stage (annotation) or section (TTS). Stable
    /// integer values — see <see cref="BlockKind"/>. Required at
    /// hydration time; default is <see cref="BlockKind.Stage"/> so a
    /// half-populated row degrades to the safer kind (no engine
    /// wiring, no generation attempt).
    /// </summary>
    [Key(0)]
    public BlockKind Kind { get; set; } = BlockKind.Stage;

    /// <summary>Display label on the block (e.g. "Patlama — diyalog kesik").</summary>
    [Key(1)]
    public string Label { get; set; } = string.Empty;

    /// <summary>Start time on the timeline, in seconds.</summary>
    [Key(2)]
    public double StartSec { get; set; }

    /// <summary>Length of the block, in seconds.</summary>
    [Key(3)]
    public double DurationSec { get; set; }

    // --- Section-only fields (null when Kind == Stage) -----------------

    /// <summary>
    /// Emotion intensity 0–100 (cool → hot). Null on stage blocks;
    /// populated on sections. Default at section construction is 50
    /// (handled by the view-model, not by this POCO — the POCO
    /// faithfully reports whatever was last persisted, including a
    /// genuine zero).
    /// </summary>
    [Key(4)]
    public int? Emotion { get; set; }

    /// <summary>
    /// Sampling temperature, typically 0.0–2.0. Null on stage blocks;
    /// populated on sections.
    /// </summary>
    [Key(5)]
    public double? Temperature { get; set; }

    /// <summary>
    /// RNG seed. 0 means "auto" (engine picks). Null on stage blocks;
    /// populated on sections.
    /// </summary>
    [Key(6)]
    public int? Seed { get; set; }

    /// <summary>
    /// What happens to <see cref="Seed"/> after each successful
    /// generation. Null on stage blocks; populated on sections.
    /// </summary>
    [Key(7)]
    public SeedAdvanceMode? SeedMode { get; set; }

    /// <summary>
    /// Engine identifier (e.g. "Chatterbox"). Null on stage blocks;
    /// empty string on sections that haven't been bound yet (the user
    /// hasn't picked from the dropdown).
    /// </summary>
    [Key(8)]
    public string? EngineId { get; set; }

    /// <summary>
    /// Voice sample reference (filename today, stable id later).
    /// Null on stage blocks; null on sections until the user
    /// drag-drops a sample onto them.
    /// </summary>
    [Key(9)]
    public string? VoiceSampleRef { get; set; }
}
