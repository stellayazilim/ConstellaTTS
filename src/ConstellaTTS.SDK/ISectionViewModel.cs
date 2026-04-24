using ConstellaTTS.Domain.Primitives;

namespace ConstellaTTS.SDK;

/// <summary>
/// Contract for a TTS-generating section — a stage with engine wiring.
/// Inherits geometry/label/colour from <see cref="IStageViewModel"/>
/// and adds the fields that drive the TTS pipeline: emotion intensity,
/// dirty flag, and the engine model binding.
///
/// A section is created with no engine model bound (<see cref="Model"/>
/// is null). The user picks an engine later via the section editor's
/// model dropdown, which instantiates the concrete <see cref="Model"/>
/// derivative and assigns it here.
/// </summary>
public interface ISectionViewModel : IStageViewModel
{
    /// <summary>Emotion intensity 0–100 (cool → hot).</summary>
    int    Emotion { get; set; }

    /// <summary>Has unsaved/ungenerated changes — shows yellow left strip.</summary>
    bool   Dirty   { get; set; }

    /// <summary>
    /// Engine-specific parameter bundle. Null until the user binds an
    /// engine via the section editor's model dropdown.
    /// </summary>
    Model? Model   { get; set; }
}
