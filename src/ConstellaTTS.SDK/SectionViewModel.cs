using CommunityToolkit.Mvvm.ComponentModel;
using ConstellaTTS.Domain.Primitives;

namespace ConstellaTTS.SDK;

/// <summary>
/// Default ViewModel for a timeline section — a block that participates
/// in the TTS pipeline. Inherits label/geometry/colour from
/// <see cref="StageViewModel"/> and adds emotion, dirty state, and the
/// engine model binding.
///
/// Instantiated with <see cref="Model"/> = null; the user binds an
/// engine later via the section editor's model dropdown.
/// </summary>
public partial class SectionViewModel : StageViewModel, ISectionViewModel
{
    /// <summary>Emotion intensity 0–100 (cool → hot).</summary>
    [ObservableProperty] private int _emotion;

    /// <summary>Has unsaved/ungenerated changes — shows yellow left strip.</summary>
    [ObservableProperty] private bool _dirty;

    /// <summary>
    /// Engine-specific parameter bundle. Null until the user binds an
    /// engine via the section editor's model dropdown.
    /// </summary>
    [ObservableProperty] private Model? _model;
}
