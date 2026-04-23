namespace ConstellaTTS.SDK.UI.Regions;

/// <summary>
/// Named region identifiers — dot-path format matches RegionControl.RegionId in XAML.
/// MainWindow.Layout.Content → only that region updates, nothing else re-mounts.
/// </summary>
public static class Regions
{
    // ── MainWindow ────────────────────────────────────────────────────────
    public const string Layout    = "MainWindow.Layout";
    public const string StatusBar = "MainWindow.StatusBar";

    // ── MainLayout ────────────────────────────────────────────────────────
    public const string Toolbar   = "MainWindow.Layout.Toolbar";
    public const string ViewTools = "MainWindow.Layout.ViewTools";
    public const string Content   = "MainWindow.Layout.Content";

    // ── DAW ───────────────────────────────────────────────────────────────
    public const string TimelineHeader = "MainWindow.Layout.Content.TimelineHeader";
    public const string TrackList      = "MainWindow.Layout.Content.TrackList";
    public const string Minimap        = "MainWindow.Layout.Content.Minimap";
}
