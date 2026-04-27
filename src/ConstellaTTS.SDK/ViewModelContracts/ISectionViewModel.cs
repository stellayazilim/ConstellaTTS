namespace ConstellaTTS.SDK.ViewModelContracts;

/// <summary>
/// Strategy for how the section's seed evolves between consecutive
/// generations. The seed is whatever's stored in <see cref="ISectionViewModel.Seed"/>;
/// the strategy controls what happens to that value AFTER a successful
/// generation completes, so the next render uses something different
/// (or the same) without the user having to touch the field manually.
///
///   · <see cref="Fixed"/>       — keep Seed exactly as-is. Reproducible
///                                renders; the same prompt always returns
///                                the same audio. Default.
///   · <see cref="Increment"/>   — +1 after each generation. Useful for
///                                exploring nearby variants “one step at
///                                a time” while staying close to the
///                                previous render's character.
///   · <see cref="Decrement"/>   — −1 after each generation. Mirror of
///                                Increment for users who like walking
///                                the seed space backwards.
///   · <see cref="Random"/>      — fresh non-zero int after each
///                                generation. Maximum variety; matches
///                                hitting the dice button automatically.
/// </summary>
public enum SeedAdvanceMode
{
    Fixed,
    Increment,
    Decrement,
    Random,
}

/// <summary>
/// Contract for a TTS-generating section — a stage with engine wiring.
/// Inherits geometry/label/colour from <see cref="IStageViewModel"/>
/// and adds the fields that drive the TTS pipeline.
///
/// <para>
/// Field set:
///   · <see cref="Emotion"/>        — 0–100 cool→hot intensity slider.
///   · <see cref="Temperature"/>    — 0.0–2.0 sampling temperature.
///   · <see cref="Seed"/>           — RNG seed; 0 means "auto" (engine picks).
///   · <see cref="SeedMode"/>       — what happens to Seed after each render.
///   · <see cref="EngineId"/>       — selected engine identifier (e.g.
///                                    "Chatterbox", "F5-TTS"). Maps to a
///                                    registered <c>IEngineCatalog</c> entry.
///   · <see cref="VoiceSampleRef"/> — reference to the voice sample that
///                                    drives the clone. Stored as a
///                                    string identifier (filename today,
///                                    a stable id later) so the section
///                                    contract doesn't pull in the audio
///                                    layer; the editor / engine resolves
///                                    it through the sample service.
///   · <see cref="Dirty"/>          — flag for "needs regeneration". Any
///                                    change to the above flips it true.
/// </para>
///
/// <para>
/// Sections start unbound (EngineId empty, VoiceSampleRef null). The
/// section editor's controls drive the user through binding them.
/// </para>
///
/// <para>
/// <b>Why a string ref instead of the Sample record itself.</b>
/// <c>Sample</c> lives in the audio SDK; reaching it from this contract
/// would invert the dependency graph (the audio layer depends on the
/// SDK, not the other way round). A string reference keeps the
/// section contract layer-agnostic — the audio service, the engine
/// adapter, and the UI all dereference it through their own
/// catalogue lookup. Engine plugin parameters (the old
/// <c>Model</c> bag) will land here as a separate, plugin-defined
/// shape once the plugin system arrives; for now the section is a
/// fixed parameter set.
/// </para>
/// </summary>
public interface ISectionViewModel : IStageViewModel
{
    /// <summary>Emotion intensity 0–100 (cool → hot).</summary>
    int Emotion { get; set; }

    /// <summary>
    /// Sampling temperature, typically 0.0–2.0. Higher = more variety,
    /// lower = more deterministic. Default 0.7 matches most engine docs.
    /// </summary>
    double Temperature { get; set; }

    /// <summary>
    /// RNG seed. 0 is treated as "auto" — the engine picks a fresh seed
    /// each generation. Non-zero values are reproducible.
    /// </summary>
    int Seed { get; set; }

    /// <summary>
    /// What happens to <see cref="Seed"/> after each successful
    /// generation. See <see cref="SeedAdvanceMode"/> for the four
    /// strategies. Defaults to <see cref="SeedAdvanceMode.Fixed"/> so
    /// renders stay reproducible until the user explicitly opts into
    /// drift.
    /// </summary>
    SeedAdvanceMode SeedMode { get; set; }

    /// <summary>
    /// Engine identifier (e.g. "Chatterbox"). Empty until the user picks.
    /// Maps to an <c>IEngineCatalog</c> entry.
    /// </summary>
    string EngineId { get; set; }

    /// <summary>
    /// Identifier of the voice sample the engine should clone the voice
    /// from. Filename (e.g. <c>"voice_lyra.wav"</c>) for the moment;
    /// resolved against the active project's catalogue at render time.
    /// Null until the user assigns one — typically by drag-dropping a
    /// sample from the Sample Library onto this section.
    /// </summary>
    string? VoiceSampleRef { get; set; }

    /// <summary>Has unsaved/ungenerated changes — shows yellow left strip.</summary>
    bool Dirty { get; set; }
}
