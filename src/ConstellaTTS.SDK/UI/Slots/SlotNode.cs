namespace ConstellaTTS.SDK.UI.Slots;

/// <summary>
/// A node in the slot tree. Tracks the slot definition, its type,
/// the currently mounted view, and any child slots exposed by that view.
/// </summary>
public sealed class SlotNode
{
    public Slot     Slot        { get; }
    public SlotType SlotType    { get; }
    public Type?    MountedView { get; private set; }

    /// <summary>Child slots populated when a Layout or Page view is mounted here.</summary>
    public SlotMap? ChildSlots { get; private set; }

    public SlotNode(Slot slot, SlotType slotType)
    {
        Slot     = slot;
        SlotType = slotType;
    }

    /// <summary>Records the mounted view type.</summary>
    public void Mount(Type viewType) => MountedView = viewType;

    /// <summary>Clears the mounted view type.</summary>
    public void Unmount() => MountedView = null;

    /// <summary>
    /// Attaches child slots exposed by the mounted Layout or Page view.
    /// Throws if this slot's type does not support child slots.
    /// </summary>
    public void AttachChildSlots(SlotMap childSlots)
    {
        if (SlotType is not (SlotType.Layout or SlotType.Page))
            throw new InvalidOperationException(
                $"Slot '{Slot.Name}' is of type {SlotType} and cannot have child slots.");

        ChildSlots = childSlots;
    }

    /// <summary>Removes the attached child slot map.</summary>
    public void DetachChildSlots() => ChildSlots = null;
}
