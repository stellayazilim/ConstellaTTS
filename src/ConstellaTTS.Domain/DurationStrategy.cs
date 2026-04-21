namespace ConstellaTTS.Domain;

/// <summary>
/// Determines how the generated audio duration is controlled.
/// </summary>
public enum DurationStrategy
{
    /// <summary>Model's natural output duration — no post-processing.</summary>
    Auto,

    /// <summary>Post-process time stretch via Rubberband.</summary>
    Rubberband,

    /// <summary>Iterative generation with speed parameter until target duration is hit.</summary>
    Interpolate,

    /// <summary>Token-count control (IndexTTS-2 style: target_tokens = target_sec * 21).</summary>
    Generate
}
