namespace ConstellaTTS.SDK.UI.Navigation;

/// <summary>
/// Fluent builder for composing navigation requests.
/// Pure data — no INavigationManager dependency.
/// </summary>
public sealed class NavigationBuilder
{
    private readonly List<NavigationRequest> _requests = [];

    public NavigationBuilder OpenWindow<TWindow>()
    {
        _requests.Add(new OpenWindowRequest(typeof(TWindow)));
        return this;
    }

    public NavigationBuilder CloseWindow<TWindow>()
    {
        _requests.Add(new CloseWindowRequest(typeof(TWindow)));
        return this;
    }

    public NavigationBuilder ShowFlyout<TFlyout>()
    {
        _requests.Add(new ShowFlyoutRequest(typeof(TFlyout)));
        return this;
    }

    public NavigationBuilder HideFlyout<TFlyout>()
    {
        _requests.Add(new HideFlyoutRequest(typeof(TFlyout)));
        return this;
    }

    /// <summary>Mount a view into a named region.</summary>
    public NavigationBuilder Mount(string regionId, Type viewType)
    {
        _requests.Add(new MountRegionRequest(regionId, viewType));
        return this;
    }

    /// <summary>Mount a view into a named region.</summary>
    public NavigationBuilder Mount<TView>(string regionId)
    {
        _requests.Add(new MountRegionRequest(regionId, typeof(TView)));
        return this;
    }

    /// <summary>Unmount the current view from a named region.</summary>
    public NavigationBuilder Unmount(string regionId)
    {
        _requests.Add(new UnmountRegionRequest(regionId));
        return this;
    }

    public NavigationRequest Build() => _requests.Count == 1
        ? _requests[0]
        : new QueueNavigationRequest(_requests);
}
