namespace ConstellaTTS.SDK.UI.Slots;

/// <summary>Defines what kind of content a slot can host.</summary>
public enum SlotType
{
    /// <summary>A top-level application window.</summary>
    Window,

    /// <summary>A layout container that declares its own child slots.</summary>
    Layout,

    /// <summary>A full-page view that may expose its own child slots when mounted.</summary>
    Page,

    /// <summary>A leaf control with no further slot nesting.</summary>
    Control
}
