namespace ConstellaTTS.SDK.UI.Slots;

/// <summary>
/// Manages the slot tree across all registered windows.
/// Windows declare their top-level slot maps at startup; plugins can extend
/// Layout and Page slots with child slot maps when mounting views.
/// </summary>
public interface ISlotService
{
    /// <summary>All registered window descriptors, keyed by window type.</summary>
    IReadOnlyDictionary<Type, WindowDescriptor> Windows { get; }

    /// <summary>Registers a window and its top-level slot map.</summary>
    void RegisterWindow(WindowDescriptor descriptor);

    /// <summary>Returns the descriptor for the given window type, or null if not registered.</summary>
    WindowDescriptor? GetWindow(Type windowType);

    /// <summary>
    /// Searches the slot tree for the named slot, recursing into child slot maps.
    /// Returns null if the window is not registered or the slot is not found.
    /// </summary>
    SlotNode? FindSlot(Type windowType, Slot slot);

    /// <summary>
    /// Mounts a view into the specified slot. For Layout and Page slots,
    /// pass childSlots to register the child slot map exposed by the mounted view.
    /// </summary>
    void Mount(Type windowType, Slot slot, Type viewType, SlotMap? childSlots = null);

    /// <summary>Unmounts the current view from a slot and detaches any child slots.</summary>
    void Unmount(Type windowType, Slot slot);
}
