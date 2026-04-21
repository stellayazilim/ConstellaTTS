using ConstellaTTS.SDK.UI.Slots;

namespace ConstellaTTS.SDK.UI.Navigation;

/// <summary>Base record for all navigation requests.</summary>
public abstract record NavigationRequest;

/// <summary>Opens a window of the specified type.</summary>
public sealed record OpenWindowRequest(Type WindowType) : NavigationRequest;

/// <summary>Closes a window of the specified type.</summary>
public sealed record CloseWindowRequest(Type WindowType) : NavigationRequest;

/// <summary>Swaps the active layout in the current window.</summary>
public sealed record SwapLayoutRequest(Type LayoutType) : NavigationRequest;

/// <summary>Mounts a view to a specific slot.</summary>
public sealed record MountSlotRequest(Slot Slot, Type ViewType) : NavigationRequest;

/// <summary>Unmounts the currently mounted view from a slot.</summary>
public sealed record UnmountSlotRequest(Slot Slot) : NavigationRequest;

/// <summary>
/// Executes multiple navigation requests sequentially as a single atomic operation.
/// Pushed to history as one entry so the entire batch is rolled back together.
/// </summary>
public sealed record QueueNavigationRequest(
    IReadOnlyList<NavigationRequest> Requests) : NavigationRequest;
