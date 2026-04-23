using Avalonia.Controls;
using Avalonia.Threading;
using ConstellaTTS.Core.UI.Regions;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.UI.Keybinds;
using ConstellaTTS.SDK.UI.Navigation;
using ConstellaTTS.SDK.UI.Regions;
using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.Core.UI.Infrastructure;

public sealed class NavigationManager(
    IServiceProvider sp,
    IRegionManager   regions,
    IHistoryManager  history,
    IKeybindManager  keybinds) : INavigationManager
{
    private readonly Dictionary<Type, FlyoutHandler> _flyouts = new();
    private sealed record FlyoutHandler(Action Show, Action Hide, Func<bool> IsVisible);

    private Window? _activeWindow;
    public  Window? ActiveWindow => _activeWindow;

    // ── Flyout registry ───────────────────────────────────────────────────

    public void RegisterFlyout(Type flyoutType, Action show, Action hide, Func<bool> isVisible) =>
        _flyouts[flyoutType] = new FlyoutHandler(show, hide, isVisible);

    // ── Navigate ──────────────────────────────────────────────────────────

    public void Navigate(NavigationRequest request)
    {
        Apply(request);
        history.Push(request);
    }

    public void Navigate(Action<NavigationBuilder> configure)
    {
        var builder = new NavigationBuilder();
        configure(builder);
        Navigate(builder.Build());
    }

    public void ApplyOnly(NavigationRequest request) => Apply(request);

    // ── Apply ─────────────────────────────────────────────────────────────

    private void Apply(NavigationRequest request)
    {
        _ = request switch
        {
            OpenWindowRequest    r => Exec(() => OpenWindow(r.WindowType)),
            CloseWindowRequest   r => Exec(() => CloseWindow(r.WindowType)),
            ShowFlyoutRequest    r when _flyouts.TryGetValue(r.FlyoutType, out var sh) => Exec(sh.Show),
            HideFlyoutRequest    r when _flyouts.TryGetValue(r.FlyoutType, out var hh) => Exec(hh.Hide),
            MountRegionRequest   r => Exec(() => MountRegion(r)),
            UnmountRegionRequest r => Exec(() => regions.Unmount(r.RegionId)),
            QueueNavigationRequest r => Exec(() =>
            {
                var openReq   = r.Requests.OfType<OpenWindowRequest>().FirstOrDefault();
                var mountReqs = r.Requests.Where(x => x is not OpenWindowRequest).ToList();

                if (openReq is not null)
                {
                    OpenWindow(openReq.WindowType);
                    foreach (var sub in mountReqs) Apply(sub);
                }
                else
                {
                    foreach (var sub in r.Requests) Apply(sub);
                }
            }),
            _ => 0
        };
    }

    // ── Window ────────────────────────────────────────────────────────────

    private void OpenWindow(Type windowType)
    {
        var window = (Window)sp.GetRequiredService(windowType);
        _activeWindow = window;

        // Track for keybinds — handler attaches on Activated
        keybinds.TrackWindow(window);

        if (!window.IsVisible) window.Show();

        regions.RegisterRegions(window);
    }

    private void CloseWindow(Type windowType)
    {
        var window = (Window)sp.GetRequiredService(windowType);
        window.Hide();
        if (_activeWindow?.GetType() == windowType)
            _activeWindow = null;
    }

    // ── Region ────────────────────────────────────────────────────────────

    private void MountRegion(MountRegionRequest r)
    {
        var view = (Control)sp.GetRequiredService(r.ViewType);

        if (!regions.HasRegion(r.RegionId) && _activeWindow is not null)
        {
            regions.RegisterRegions(_activeWindow);

            if (!regions.HasRegion(r.RegionId))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    regions.RegisterRegions(_activeWindow);
                    regions.Mount(r.RegionId, view);
                }, DispatcherPriority.Loaded);
                return;
            }
        }

        regions.Mount(r.RegionId, view);
    }

    private static int Exec(Action action) { action(); return 0; }
}
