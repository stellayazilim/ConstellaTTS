using CommunityToolkit.Mvvm.ComponentModel;
using ConstellaTTS.Domain;
using ConstellaTTS.SDK.Primitives;

namespace ConstellaTTS.SDK;

/// <summary>
/// Abstract base ViewModel for all section types.
/// Cannot be instantiated directly — derive for each engine/model combination.
/// </summary>
public abstract partial class SectionViewModel(Section section) : ViewModel
{
    public string  Text        { get; set; } = section.Text;
    public string  Engine      { get; set; } = section.Engine;
    public float   Emotion     { get; set; } = section.Emotion;
    public float?  DurOverride { get; set; } = section.DurOverride;
    public DurationStrategy DurStrategy { get; set; } = section.DurStrategy;
    public bool    IsStageDir  { get; set; } = section.IsStageDir;

    // [ObservableProperty] public  partial int Seed { get; set; }
    // [ObservableProperty] public partial float   StartSec      { get; set; }
    // [ObservableProperty] public partial string? VoiceRef      { get; set; }
    // [ObservableProperty] public partial bool    Dirty         { get; set; }
    // [ObservableProperty] public partial float?  Wer           { get; set; }
    // [ObservableProperty] public partial string? GeneratedFile { get; set; }
}

