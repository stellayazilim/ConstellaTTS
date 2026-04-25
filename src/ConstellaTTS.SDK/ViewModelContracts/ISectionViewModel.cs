using ConstellaTTS.Domain;
using ConstellaTTS.Domain.Primitives;

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
/// Field set:
///   · <see cref="Emotion"/>      — 0–100 cool→hot intensity slider.
///   · <see cref="Temperature"/>  — 0.0–2.0 sampling temperature.
///   · <see cref="Seed"/>         — RNG seed; 0 means "auto" (engine picks).
///   · <see cref="EngineId"/>     — selected engine identifier (e.g.
///                                  "Chatterbox", "F5-TTS"). Maps to a
///                                  registered <c>IEngineCatalog</c> entry.
///   · <see cref="VoiceSample"/>  — reference audio sample driving the
///                                  voice clone. Domain entity, not a VM.
///   · <see cref="Dirty"/>        — flag for "needs regeneration". Any
///                                  change to the above flips it true.
///   · <see cref="Model"/>        — engine-specific extra params bundle.
///                                  Optional, populated by the engine's
///                                  plugin if it has bespoke knobs.
///
/// Sections start unbound (EngineId empty, Model null, VoiceSample null).
/// The section editor's controls drive the user through binding them.
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
    /// Maps to a <see cref="IEngineCatalog"/> entry.
    /// </summary>
    string EngineId { get; set; }

    /// <summary>
    /// Reference audio sample the engine should clone the voice from.
    /// Null until the user selects one from the sample library.
    /// </summary>
    Sample? VoiceSample { get; set; }

    /// <summary>Has unsaved/ungenerated changes — shows yellow left strip.</summary>
    bool Dirty { get; set; }

    /// <summary>
    /// Engine-specific parameter bundle. Null until the user binds an
    /// engine via the section editor's model dropdown.
    /// </summary>
    Model? Model { get; set; }
}
