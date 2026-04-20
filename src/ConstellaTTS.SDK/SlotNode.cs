namespace ConstellaTTS.SDK;

/// <summary>
/// A node in the slot tree. Holds a slot definition, its type,
/// currently mounted view, and optional child slot map (for Layout/Page slots).
/// </summary>
public sealed class SlotNode
{
    public Slot     Slot        { get; }
    public SlotType SlotType    { get; }
    public Type?    MountedView { get; private set; }

    /// <summary>
    /// Child slots — populated when a Layout or Page is mounted here.
    /// </summary>
    public SlotMap? ChildSlots { get; private set; }

    public SlotNode(Slot slot, SlotType slotType)
    {
        Slot     = slot;
        SlotType = slotType;
    }

    public void Mount(Type viewType) => MountedView = viewType;
    public void Unmount()            => MountedView = null;

    /// <summary>
    /// Attaches child slots when a Layout or Page is mounted.
    /// </summary>
    public void AttachChildSlots(SlotMap childSlots)
    {
        if (SlotType is not (SlotType.Layout or SlotType.Page))
            throw new InvalidOperationException(
                $"Slot '{Slot.Name}' is of type {SlotType} and cannot have child slots.");

        ChildSlots = childSlots;
    }

    public void DetachChildSlots() => ChildSlots = null;
}
