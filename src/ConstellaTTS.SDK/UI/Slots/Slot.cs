namespace ConstellaTTS.SDK.UI.Slots;

/// <summary>Identifies a named UI slot within a window or layout.</summary>
public readonly record struct Slot(string Name);

/// <summary>Built-in platform slots registered by the Core module at startup.</summary>
public static partial class Slots
{
    public static readonly Slot Toolbar   = new("Region.Toolbar");
    public static readonly Slot ViewTools = new("Region.ViewTools");
    public static readonly Slot Content   = new("Region.Content");
}
