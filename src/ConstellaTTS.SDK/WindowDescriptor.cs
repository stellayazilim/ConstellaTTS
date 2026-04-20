namespace ConstellaTTS.SDK;

/// <summary>
/// Describes a window and its top-level slot map.
/// Registered with <see cref="ISlotService"/> at startup.
/// </summary>
public sealed class WindowDescriptor
{
    public Type    WindowType { get; }
    public SlotMap SlotMap   { get; }

    public WindowDescriptor(Type windowType, SlotMap slotMap)
    {
        WindowType = windowType;
        SlotMap    = slotMap;
    }
}
