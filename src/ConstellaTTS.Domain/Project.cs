namespace ConstellaTTS.Domain;

/// <summary>
/// The root project model. Serialized as project.json inside a .wwv archive.
/// </summary>
public class Project
{
    /// <summary>Project display name.</summary>
    public string Name { get; set; } = "Untitled Project";

    /// <summary>Format version for migration support.</summary>
    public int Version { get; set; } = 1;

    /// <summary>Creation timestamp (UTC).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Last modified timestamp (UTC).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>All tracks in the project, ordered by Id.</summary>
    public List<Track> Tracks { get; set; } = new();

    /// <summary>All sections across all tracks.</summary>
    public List<Section> Sections { get; set; } = new();

    /// <summary>Pixels per second — controls timeline zoom level.</summary>
    public float PixelsPerSecond { get; set; } = 100f;

    /// <summary>Active TTS engine id — default for new tracks.</summary>
    public string DefaultEngine { get; set; } = string.Empty;
}
