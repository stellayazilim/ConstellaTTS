using ConstellaTTS.Domain.Primitives;

namespace ConstellaTTS.Domain;


/// <summary>
/// A reference audio sample with its preprocessed representations per model.
/// </summary>
public class Sample(Guid id) : Entity<Guid>(id)
{
    /// <summary>Path to the raw audio file.</summary>
    public string RawAudioPath { get; set; } = string.Empty;

    /// <summary>Duration of the raw audio.</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Preprocessed data per TTS model — one entry per model type.
    /// Equality is handled by SamplePreProcessedData.Entity&lt;Type&gt; identity.
    /// </summary>
    public HashSet<SamplePreProcessedData> PreprocessedData { get; set; } = new();

    public override IEnumerable<object> GetEqualityComponents()
    {
        yield return Id;
    }
}
