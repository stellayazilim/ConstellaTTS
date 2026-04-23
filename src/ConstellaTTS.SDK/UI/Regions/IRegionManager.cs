namespace ConstellaTTS.SDK.UI.Regions;

/// <summary>
/// Manages named UI regions across all open windows.
/// Regions are discovered by scanning the visual tree for RegionControl instances.
/// Mount/Unmount only affects the targeted region — nothing else re-renders.
/// </summary>
public interface IRegionManager
{
    /// <summary>
    /// Scan the visual tree of a window and register all RegionControl instances.
    /// Called by NavigationManager when a window is opened.
    /// </summary>
    void RegisterRegions(Avalonia.Controls.Window window);

    /// <summary>Mount a view into the named region.</summary>
    void Mount(string regionId, Avalonia.Controls.Control view);

    /// <summary>Unmount the current view from the named region.</summary>
    void Unmount(string regionId);

    /// <summary>Returns true if a region with the given ID is registered.</summary>
    bool HasRegion(string regionId);
}
