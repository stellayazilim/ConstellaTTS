using ConstellaTTS.Domain.Primitives;

namespace ConstellaTTS.Domain;

/// <summary>
/// Base section — holds all engine-agnostic fields.
/// Kept as non-generic so collections can hold mixed section types.
/// </summary>
public abstract partial class Section
{
    public string Id       { get; set; } = Guid.NewGuid().ToString();
    public int    Track    { get; set; }
    public float  StartSec { get; set; }
    public string Text     { get; set; } = string.Empty;
    public string Engine   { get; set; } = string.Empty;
    public int    Seed     { get; set; }
    public float  Emotion  { get; set; }

    public string? VoiceRef           { get; set; }
    public string? VoiceRefTranscript { get; set; }
    public float?  DurOverride        { get; set; }

    public DurationStrategy DurStrategy   { get; set; } = DurationStrategy.Auto;
    public string?          GeneratedFile { get; set; }
    public float?           Wer           { get; set; }
    public bool             Dirty         { get; set; } = true;
    public bool             IsStageDir    { get; set; }
}

/// <summary>
/// Generic section — carries engine-specific model parameters.
/// </summary>
public class Section<TModel> : Section where TModel : Model
{
    /// <summary>
    /// Engine-specific parameters provided by the active plugin.
    /// </summary>
    public TModel? Model { get; set; }
}
