namespace ConstellaTTS.SDK.UI.Slots;

/// <summary>
/// Describes a window and its top-level slot map.
/// Registered with ISlotService at startup so the slot tree can be built.
/// </summary>
public sealed class WindowDescriptor(Type windowType, SlotMap slotMap)
{
    public Type    WindowType { get; } = windowType;
    public SlotMap SlotMap    { get; } = slotMap;
}
