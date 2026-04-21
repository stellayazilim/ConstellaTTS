namespace ConstellaTTS.Domain;

/// <summary>
/// A named voice track on the timeline.
/// Each track has its own engine and optional global voice reference.
/// </summary>
public class Track
{
    /// <summary>Unique track index.</summary>
    public int Id { get; set; }

    /// <summary>Display name (e.g. "Lyra", "Narrator").</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Default TTS engine for sections on this track.</summary>
    public string Engine { get; set; } = string.Empty;

    /// <summary>Global voice reference path — applied to all sections unless overridden.</summary>
    public string? GlobalRef { get; set; }

    /// <summary>Track color on the timeline (hex string).</summary>
    public string Color { get; set; } = "#7c6af7";

    /// <summary>Whether the track is expanded in the UI.</summary>
    public bool Expanded { get; set; } = true;

    /// <summary>
    /// Track-level plugin parameters — inherited by sections unless overridden.
    /// </summary>
    public Dictionary<string, object> TrackParameters { get; set; } = new();
}
