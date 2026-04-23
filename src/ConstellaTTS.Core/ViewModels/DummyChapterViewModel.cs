namespace ConstellaTTS.Core.ViewModels;

/// <summary>
/// Dummy chapter (section group) — replaced by real domain model later.
/// </summary>
public sealed class DummyChapterViewModel
{
    public string Name       { get; init; } = string.Empty;
    public string Color      { get; init; } = "#7C6AF7";
    public string TrackCount { get; init; } = "2 track";
    public string Duration   { get; init; } = "0:32";
    public string Meta       => $"{TrackCount} · {Duration}";
}
