namespace ConstellaTTS.SDK;

/// <summary>
/// Fluent builder for composing navigation requests.
/// </summary>
public sealed class NavigationBuilder
{
    private readonly List<NavigationRequest> _requests = [];

    /// <summary>Opens a window of the specified type.</summary>
    public NavigationBuilder OpenWindow<TWindow>()
    {
        _requests.Add(new OpenWindowRequest(typeof(TWindow)));
        return this;
    }

    /// <summary>Closes a window of the specified type.</summary>
    public NavigationBuilder CloseWindow<TWindow>()
    {
        _requests.Add(new CloseWindowRequest(typeof(TWindow)));
        return this;
    }

    /// <summary>Swaps the active layout.</summary>
    public NavigationBuilder SwapLayout<TLayout>()
    {
        _requests.Add(new SwapLayoutRequest(typeof(TLayout)));
        return this;
    }

    /// <summary>Mounts a view to a slot.</summary>
    public NavigationBuilder Mount(Slot slot, Type viewType)
    {
        _requests.Add(new MountSlotRequest(slot, viewType));
        return this;
    }

    /// <summary>Unmounts the current view from a slot.</summary>
    public NavigationBuilder Unmount(Slot slot)
    {
        _requests.Add(new UnmountSlotRequest(slot));
        return this;
    }

    internal NavigationRequest Build() => _requests.Count == 1
        ? _requests[0]
        : new QueueNavigationRequest(_requests);
}
