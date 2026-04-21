using ConstellaTTS.SDK.UI.Slots;

namespace ConstellaTTS.Core.UI.Infrastructure;

/// <summary>
/// Manages the slot tree across all registered windows.
/// Supports recursive child slot maps for Layout and Page slots.
/// </summary>
public sealed class SlotService : ISlotService
{
    private readonly Dictionary<Type, WindowDescriptor> _windows = new();

    /// <inheritdoc/>
    public IReadOnlyDictionary<Type, WindowDescriptor> Windows => _windows;

    /// <inheritdoc/>
    public void RegisterWindow(WindowDescriptor descriptor)
    {
        if (!_windows.TryAdd(descriptor.WindowType, descriptor))
            throw new InvalidOperationException(
                $"Window '{descriptor.WindowType.Name}' is already registered.");
    }

    /// <inheritdoc/>
    public WindowDescriptor? GetWindow(Type windowType) =>
        _windows.GetValueOrDefault(windowType);

    /// <inheritdoc/>
    public SlotNode? FindSlot(Type windowType, Slot slot)
    {
        if (!_windows.TryGetValue(windowType, out var descriptor))
            return null;

        return FindInMap(descriptor.SlotMap, slot);
    }

    /// <inheritdoc/>
    public void Mount(Type windowType, Slot slot, Type viewType, SlotMap? childSlots = null)
    {
        var node = FindSlot(windowType, slot)
            ?? throw new InvalidOperationException(
                $"Slot '{slot.Name}' not found in window '{windowType.Name}'.");

        node.Mount(viewType);

        if (childSlots is not null)
            node.AttachChildSlots(childSlots);
    }

    /// <inheritdoc/>
    public void Unmount(Type windowType, Slot slot)
    {
        var node = FindSlot(windowType, slot)
            ?? throw new InvalidOperationException(
                $"Slot '{slot.Name}' not found in window '{windowType.Name}'.");

        node.Unmount();
        node.DetachChildSlots();
    }

    private static SlotNode? FindInMap(SlotMap map, Slot slot)
    {
        if (map.Get(slot) is { } found)
            return found;

        foreach (var node in map.Nodes.Values)
        {
            if (node.ChildSlots is not null)
            {
                var result = FindInMap(node.ChildSlots, slot);
                if (result is not null) return result;
            }
        }

        return null;
    }
}
