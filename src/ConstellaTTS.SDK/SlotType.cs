namespace ConstellaTTS.SDK;

/// <summary>
/// Defines what type of content a slot can host.
/// </summary>
public enum SlotType
{
    /// <summary>A top-level application window.</summary>
    Window,

    /// <summary>A layout container — defines its own child slots.</summary>
    Layout,

    /// <summary>A full page view — can expose its own slots when mounted.</summary>
    Page,

    /// <summary>A leaf control — cannot contain further slots.</summary>
    Control
}
