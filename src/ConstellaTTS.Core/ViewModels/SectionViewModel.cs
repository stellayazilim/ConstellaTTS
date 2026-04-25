using CommunityToolkit.Mvvm.ComponentModel;
using ConstellaTTS.Domain;
using ConstellaTTS.Domain.Primitives;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// Default ViewModel for a timeline section — a block that participates
/// in the TTS pipeline. Inherits label/geometry/colour from
/// <see cref="StageViewModel"/> and adds the engine-binding fields.
///
/// Defaults are chosen to match common engine docs so a freshly-drawn
/// section is usable without touching every knob:
///   · Temperature 0.7 — most engines' "neutral" setting.
///   · Seed 0          — auto-pick per generation.
///   · Emotion 50      — middle of the cool→hot range.
///   · EngineId ""     — unbound; user picks from the dropdown.
///
/// Every settable property is wrapped in <c>[ObservableProperty]</c>;
/// <see cref="Dirty"/> is its own observable so future hooks can mark
/// the block "needs regeneration" when any input changes (a partial
/// <c>OnXChanged</c> hook for each input would be the place — left out
/// here so the editor can drive Dirty explicitly during user edits).
/// </summary>
public partial class SectionViewModel : StageViewModel, ISectionViewModel
{
    [ObservableProperty] private int    _emotion      = 50;
    [ObservableProperty] private double _temperature  = 0.7;
    [ObservableProperty] private int    _seed         = 0;
    [ObservableProperty] private SeedAdvanceMode _seedMode = SeedAdvanceMode.Fixed;
    [ObservableProperty] private string _engineId     = string.Empty;
    [ObservableProperty] private Sample? _voiceSample;
    [ObservableProperty] private bool   _dirty;
    [ObservableProperty] private Model? _model;
}
