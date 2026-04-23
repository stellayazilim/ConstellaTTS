namespace ConstellaTTS.SDK.UI.Slots;

/// <summary>Identifies a named UI region within a window or layout.</summary>
public readonly record struct Slot(string Name);

/// <summary>
/// Built-in platform slots. Name matches RegionId.Value in XAML.
/// Path convention: WindowName.LayoutName.SlotName
/// </summary>
public static partial class Slots
{
    // ── MainWindow ────────────────────────────────────────────────────────
    public static readonly Slot Layout    = new("MainWindow.Layout");
    public static readonly Slot StatusBar = new("MainWindow.StatusBar");

    // ── MainWindow.Layout ─────────────────────────────────────────────────
    public static readonly Slot Toolbar        = new("MainWindow.Layout.Toolbar");
    public static readonly Slot ViewTools      = new("MainWindow.Layout.ViewTools");
    public static readonly Slot Content        = new("MainWindow.Layout.Content");
    public static readonly Slot TimelineHeader = new("MainWindow.Layout.TimelineHeader");
    public static readonly Slot Minimap        = new("MainWindow.Layout.Minimap");
}
