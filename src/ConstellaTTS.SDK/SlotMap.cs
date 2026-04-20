namespace ConstellaTTS.SDK;

/// <summary>
/// A collection of slots belonging to a window, layout, or page.
/// </summary>
public sealed class SlotMap
{
    private readonly Dictionary<string, SlotNode> _nodes = new();

    public IReadOnlyDictionary<string, SlotNode> Nodes => _nodes;

    /// <summary>
    /// Adds a slot node to this map. Slot names must be unique within a map.
    /// </summary>
    public SlotMap Add(Slot slot, SlotType slotType)
    {
        var node = new SlotNode(slot, slotType);

        if (!_nodes.TryAdd(slot.Name, node))
            throw new InvalidOperationException(
                $"Slot '{slot.Name}' already exists in this map.");

        return this;
    }

    public SlotNode? Get(Slot slot) =>
        _nodes.GetValueOrDefault(slot.Name);

    public SlotNode? Get(string slotName) =>
        _nodes.GetValueOrDefault(slotName);
}
