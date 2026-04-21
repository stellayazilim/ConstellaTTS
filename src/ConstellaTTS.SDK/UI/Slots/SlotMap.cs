namespace ConstellaTTS.SDK.UI.Slots;

/// <summary>
/// An ordered collection of slots belonging to a window, layout, or page.
/// Slot names must be unique within the same map.
/// </summary>
public sealed class SlotMap
{
    private readonly Dictionary<string, SlotNode> _nodes = new();

    /// <summary>All slot nodes in this map, keyed by slot name.</summary>
    public IReadOnlyDictionary<string, SlotNode> Nodes => _nodes;

    /// <summary>
    /// Adds a slot to this map. Throws if a slot with the same name already exists.
    /// </summary>
    public SlotMap Add(Slot slot, SlotType slotType)
    {
        var node = new SlotNode(slot, slotType);

        if (!_nodes.TryAdd(slot.Name, node))
            throw new InvalidOperationException(
                $"Slot '{slot.Name}' already exists in this map.");

        return this;
    }

    /// <summary>Returns the node for the given slot, or null if not found.</summary>
    public SlotNode? Get(Slot slot) => _nodes.GetValueOrDefault(slot.Name);

    /// <summary>Returns the node for the given slot name, or null if not found.</summary>
    public SlotNode? Get(string slotName) => _nodes.GetValueOrDefault(slotName);
}
