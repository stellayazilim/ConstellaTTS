using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.UI.Keybinds;
using ConstellaTTS.SDK.UI.Navigation;
using ConstellaTTS.SDK.UI.Regions;
using ConstellaTTS.SDK.ViewModelContracts;
using Microsoft.Extensions.DependencyInjection;

namespace ConstellaTTS.Core.Managers;

public sealed class NavigationManager(
    IServiceProvider sp,
    IRegionManager   regions,
    IHistoryManager  history,
    IKeybindManager  keybinds) : INavigationManager
{
    private readonly Dictionary<Type, FlyoutHandler> _flyouts       = new();
    private readonly Dictionary<Type, Window>         _openedWindows = new();
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
            OpenWindowRequest    r => Exec(() => OpenWindow(r.WindowType, r.Data)),
            CloseWindowRequest   r => Exec(() => CloseWindow(r.WindowType)),
            HideWindowRequest    r => Exec(() => HideWindow(r.WindowType)),
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
                    OpenWindow(openReq.WindowType, openReq.Data);
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

    private void OpenWindow(Type windowType, object? data = null)
    {
        // Resolve a fresh instance from DI. For singletons that's
        // the registered instance; for transients it's a new one
        // every call — which is exactly what we want for the
        // launcher (each open builds a fresh VM and view, every
        // close lets the GC reclaim them).
        var window = (Window)sp.GetRequiredService(windowType);
        _activeWindow = window;

        // Track the live instance keyed by type so a later
        // CloseWindow / HideWindow request operates on this exact
        // window, not a fresh DI resolve. Without this, closing a
        // transient does nothing visible — sp.GetRequiredService
        // hands out a brand-new, never-shown instance and Close()
        // on that is a no-op for the user. Re-opening the same
        // type before the previous instance was torn down would
        // overwrite the entry; that's fine because the navigation
        // model is single-document and the previous window is
        // expected to be closed by the same request flow.
        _openedWindows[windowType] = window;

        // Keep the desktop lifetime's MainWindow in sync with the
        // window the user is actually looking at. Avalonia's classic
        // desktop lifetime (default ShutdownMode = OnMainWindowClose)
        // ties application shutdown to the lifetime's MainWindow
        // closing — if we don't update this on transitions like
        // launcher → DAW, the lifetime keeps pointing at the original
        // window (the launcher, hidden) and closing the new one
        // (MainWindow) doesn't shut the app down. The Hide()-based
        // CloseWindow path further compounds the issue: a hidden
        // window is still "open" from the lifetime's perspective.
        // Setting MainWindow to whatever we just opened gives the
        // lifetime a sensible anchor for shutdown.
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = window;

        // Track for keybinds — handler attaches on Activated
        keybinds.TrackWindow(window);

        // Forward the navigation payload to the window's view-model
        // BEFORE Show(). The first round of binding evaluation runs as
        // part of Show() — if GetParams ran after, any non-observable
        // property the VM populated from `data` would still be at its
        // default when the bindings first read it, and stay that way
        // because nothing raises PropertyChanged afterwards. Doing the
        // hand-off pre-Show keeps simple seed-style properties (a
        // plain `IConstellaProject` getter, for example) usable as
        // binding sources without forcing every navigation-receiving
        // VM into [ObservableProperty].
        //
        // The cast tolerates a missing or non-ViewModel DataContext
        // (legacy windows, design-time fallbacks) by simply skipping
        // the call — base ViewModel.GetParams is a no-op anyway.
        if (window.DataContext is ViewModel vm)
            vm.GetParams(data);

        if (!window.IsVisible) window.Show();

        regions.RegisterRegions(window);
    }

    private void CloseWindow(Type windowType)
    {
        // Look up the live instance the most recent OpenWindow
        // tracked. Falls back to DI for the singleton-not-currently-
        // tracked case (a window the user opened earlier in some
        // flow that didn't go through the registry, or a singleton
        // whose type happens to be requested before any Open call).
        // For transients, the registry hit is the only correct
        // resolution — DI would build a fresh instance and Close()
        // on it wouldn't touch the visible window.
        var window = _openedWindows.TryGetValue(windowType, out var tracked)
            ? tracked
            : (Window)sp.GetRequiredService(windowType);

        // Close() (not Hide()) so transient windows actually leave
        // the visual tree and become eligible for GC. HideWindow is
        // the separate path for singletons that need to preserve
        // their state across reopens.
        window.Close();

        _openedWindows.Remove(windowType);
        if (_activeWindow?.GetType() == windowType)
            _activeWindow = null;
    }

    private void HideWindow(Type windowType)
    {
        // Same instance-resolution rule as CloseWindow — prefer the
        // tracked instance so a transient's live window is the one
        // we operate on. Hide leaves the instance intact, so DI
        // singletons that go through here can be reopened later via
        // OpenWindow with their view-model state, scroll positions,
        // and any in-flight async work intact across the round
        // trip. The tracking entry stays in place — a hidden window
        // is still "the live instance for its type", and a future
        // reopen via OpenWindow will overwrite it with whatever DI
        // hands back (the same singleton or a fresh transient).
        var window = _openedWindows.TryGetValue(windowType, out var tracked)
            ? tracked
            : (Window)sp.GetRequiredService(windowType);

        window.Hide();

        if (_activeWindow?.GetType() == windowType)
            _activeWindow = null;
    }

    // ── Region ────────────────────────────────────────────────────────────

    private void MountRegion(MountRegionRequest r)
    {
        var view = (Control)sp.GetRequiredService(r.ViewType);

        // Always re-scan the active window before each mount. Earlier
        // mounts may have just inserted *new* RegionControls (e.g.
        // mounting MainLayout adds the Toolbar / ViewTools / Content
        // regions it owns), and those weren't in the visual tree when
        // OpenWindow first scanned. Without a fresh scan, subsequent
        // mount calls into those regions silently no-op — the symptom
        // is "the toolbar is empty even though Bootstrap mounts it".
        if (_activeWindow is not null)
            regions.RegisterRegions(_activeWindow);

        if (!regions.HasRegion(r.RegionId) && _activeWindow is not null)
        {
            // Region still not visible: layout hasn't completed yet for
            // a freshly-created window. Defer one frame and rescan.
            Dispatcher.UIThread.Post(() =>
            {
                regions.RegisterRegions(_activeWindow);
                regions.Mount(r.RegionId, view);
            }, DispatcherPriority.Loaded);
            return;
        }

        regions.Mount(r.RegionId, view);
    }

    private static int Exec(Action action) { action(); return 0; }
}
