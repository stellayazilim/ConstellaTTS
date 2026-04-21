using ConstellaTTS.SDK.UI.Slots;

namespace ConstellaTTS.SDK.UI.Navigation;

/// <summary>
/// Fluent builder for composing one or more navigation requests into a single operation.
/// Pass the result of Build() to INavigationManager.Navigate.
/// </summary>
public sealed class NavigationBuilder
{
    private readonly List<NavigationRequest> _requests = [];

    /// <summary>Queues a request to open a window of the specified type.</summary>
    public NavigationBuilder OpenWindow<TWindow>()
    {
        _requests.Add(new OpenWindowRequest(typeof(TWindow)));
        return this;
    }

    /// <summary>Queues a request to close a window of the specified type.</summary>
    public NavigationBuilder CloseWindow<TWindow>()
    {
        _requests.Add(new CloseWindowRequest(typeof(TWindow)));
        return this;
    }

    /// <summary>Queues a request to swap the active layout.</summary>
    public NavigationBuilder SwapLayout<TLayout>()
    {
        _requests.Add(new SwapLayoutRequest(typeof(TLayout)));
        return this;
    }

    /// <summary>Queues a request to mount a view into the specified slot.</summary>
    public NavigationBuilder Mount(Slot slot, Type viewType)
    {
        _requests.Add(new MountSlotRequest(slot, viewType));
        return this;
    }

    /// <summary>Queues a request to unmount the current view from the specified slot.</summary>
    public NavigationBuilder Unmount(Slot slot)
    {
        _requests.Add(new UnmountSlotRequest(slot));
        return this;
    }

    /// <summary>
    /// Builds the navigation request. Returns a single request if only one was queued,
    /// or a QueueNavigationRequest wrapping all queued requests.
    /// </summary>
    public NavigationRequest Build() => _requests.Count == 1
        ? _requests[0]
        : new QueueNavigationRequest(_requests);
}
