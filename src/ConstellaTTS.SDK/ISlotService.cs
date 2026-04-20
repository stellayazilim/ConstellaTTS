namespace ConstellaTTS.SDK;

/// <summary>
/// Manages the slot tree across all registered windows.
/// Windows declare their slot maps at startup; plugins can extend
/// Layout and Page slots with child slot maps when they mount.
/// </summary>
public interface ISlotService
{
    /// <summary>All registered window descriptors.</summary>
    IReadOnlyDictionary<Type, WindowDescriptor> Windows { get; }

    /// <summary>
    /// Registers a window and its top-level slot map.
    /// </summary>
    void RegisterWindow(WindowDescriptor descriptor);

    /// <summary>
    /// Returns the descriptor for the given window type, or null.
    /// </summary>
    WindowDescriptor? GetWindow(Type windowType);

    /// <summary>
    /// Finds a slot node anywhere in the slot tree for the given window.
    /// Searches recursively through child slot maps.
    /// </summary>
    SlotNode? FindSlot(Type windowType, Slot slot);

    /// <summary>
    /// Mounts a view to a slot. If the slot is Layout or Page,
    /// optionally attach child slots exposed by the mounted view.
    /// </summary>
    void Mount(Type windowType, Slot slot, Type viewType, SlotMap? childSlots = null);

    /// <summary>
    /// Unmounts the current view from a slot and detaches any child slots.
    /// </summary>
    void Unmount(Type windowType, Slot slot);
}
