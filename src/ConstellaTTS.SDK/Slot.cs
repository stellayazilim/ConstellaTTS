namespace ConstellaTTS.SDK;

/// <summary>
/// Identifies a UI slot by name.
/// </summary>
public readonly record struct Slot(string Name);

/// <summary>
/// Built-in platform slots.
/// </summary>
public static class Slots
{
    public static readonly Slot Toolbar   = new("Region.Toolbar");
    public static readonly Slot ViewTools = new("Region.ViewTools");
    public static readonly Slot Content   = new("Region.Content");
}
