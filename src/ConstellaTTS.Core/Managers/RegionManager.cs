using Avalonia.Controls;
using Avalonia.VisualTree;
using ConstellaTTS.Core.Controls;
using ConstellaTTS.SDK.UI.Regions;

namespace ConstellaTTS.Core.Managers;

/// <summary>
/// Scans windows for RegionControl instances and mounts views by RegionId.
/// </summary>
public sealed class RegionManager : IRegionManager
{
    private readonly Dictionary<string, RegionControl> _regions = new();

    public void RegisterRegions(Window window)
    {
        foreach (var region in window.GetVisualDescendants().OfType<RegionControl>())
            if (region.RegionId is { } id)
                _regions[id] = region;
    }

    public void Mount(string regionId, Control view)
    {
        if (_regions.TryGetValue(regionId, out var region))
            region.Content = view;
    }

    public void Unmount(string regionId)
    {
        if (_regions.TryGetValue(regionId, out var region))
            region.Content = null;
    }

    public bool HasRegion(string regionId) => _regions.ContainsKey(regionId);
}
