namespace ConstellaTTS.SDK.App;

/// <summary>
/// Discriminator for the polymorphism between annotation-only blocks
/// (<see cref="Stage"/>) and TTS-generating blocks (<see cref="Section"/>)
/// inside a single flat <see cref="BlockData"/> POCO.
///
/// <para>
/// <b>Why a discriminator instead of a class hierarchy.</b> MessagePack's
/// <c>[Union]</c> attribute does support polymorphic serialisation, but
/// it requires every implementer to register at the base type with a
/// stable integer tag, and every reader to know the full closed set of
/// subclasses. That coupling is exactly what makes new block kinds
/// expensive to add. A flat POCO with a <see cref="BlockKind"/> field
/// and nullable section-only members serialises with plain integer
/// keys, evolves the same way every other manifest field does (add
/// new keys, never reuse old ones), and lets the few code paths that
/// care about the difference (the editor, the engine adapter) branch
/// on <c>Kind</c> at the call site instead of dispatching through a
/// type hierarchy.
/// </para>
///
/// <para>
/// <b>Stable wire values.</b> The integers below are part of the
/// on-disk format. New kinds take fresh values; existing values stay
/// pinned even if a kind is dropped. <see cref="MessagePack.MessagePackObjectAttribute"/>
/// serialises enums by their underlying integer, so renaming a member
/// is free as long as the number doesn't change.
/// </para>
/// </summary>
public enum BlockKind
{
    /// <summary>
    /// Pure timeline annotation — geometry and label only, no engine
    /// wiring. The section-only members on <see cref="BlockData"/>
    /// (<c>Emotion</c>, <c>Temperature</c>, <c>Seed</c>, <c>SeedMode</c>,
    /// <c>EngineId</c>, <c>VoiceSampleRef</c>) are null for this kind.
    /// </summary>
    Stage = 0,

    /// <summary>
    /// TTS-generating block — carries the full engine parameter set
    /// in addition to the geometry/label. The section-only members on
    /// <see cref="BlockData"/> are populated for this kind.
    /// </summary>
    Section = 1,
}
