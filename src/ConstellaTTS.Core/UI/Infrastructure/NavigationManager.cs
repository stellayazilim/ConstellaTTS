using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.UI.Navigation;
using ConstellaTTS.SDK.UI.Slots;
using ConstellaTTS.SDK.UI.Windowing;

namespace ConstellaTTS.Core.UI.Infrastructure;

/// <summary>
/// Applies navigation requests, captures rollback snapshots, and pushes them to history.
/// Each Navigate call is reversible — the manager snapshots the current state before
/// applying the request so it can be restored on rollback.
/// </summary>
public sealed class NavigationManager(
    ISlotService slotService,
    IWindowManager windowManager,
    IHistoryManager historyManager) : INavigationManager
{
    /// <inheritdoc/>
    public void Navigate(NavigationRequest request)
    {
        var rollback = BuildRollback(request);
        Apply(request);
        historyManager.Push(rollback);
    }

    /// <inheritdoc/>
    public void Navigate(Action<NavigationBuilder> configure)
    {
        var builder = new NavigationBuilder();
        configure(builder);
        Navigate(builder.Build());
    }

    private void Apply(NavigationRequest request)
    {
        switch (request)
        {
            case OpenWindowRequest r:
                windowManager.Open(r.WindowType);
                break;

            case CloseWindowRequest r:
                windowManager.Close(r.WindowType);
                break;

            case MountSlotRequest r:
                slotService.Mount(windowManager.ActiveWindowType, r.Slot, r.ViewType);
                break;

            case UnmountSlotRequest r:
                slotService.Unmount(windowManager.ActiveWindowType, r.Slot);
                break;

            case SwapLayoutRequest r:
                slotService.Mount(windowManager.ActiveWindowType, Slots.Content, r.LayoutType);
                break;

            case QueueNavigationRequest r:
                foreach (var sub in r.Requests)
                    Apply(sub);
                break;
        }
    }

    private NavigationHistoryEntry BuildRollback(NavigationRequest request) =>
        new(this, SnapshotCurrentState(request));

    private NavigationRequest SnapshotCurrentState(NavigationRequest incoming)
    {
        var activeWindow = windowManager.ActiveWindowType;

        return incoming switch
        {
            MountSlotRequest r => new QueueNavigationRequest(
            [
                slotService.FindSlot(activeWindow, r.Slot)?.MountedView is { } prev
                    ? new MountSlotRequest(r.Slot, prev)
                    : new UnmountSlotRequest(r.Slot)
            ]),

            UnmountSlotRequest r => new QueueNavigationRequest(
            [
                slotService.FindSlot(activeWindow, r.Slot)?.MountedView is { } prev
                    ? new MountSlotRequest(r.Slot, prev)
                    : new UnmountSlotRequest(r.Slot)
            ]),

            OpenWindowRequest r  => new CloseWindowRequest(r.WindowType),
            CloseWindowRequest r => new OpenWindowRequest(r.WindowType),

            QueueNavigationRequest r => new QueueNavigationRequest(
                r.Requests.Select(SnapshotCurrentState).ToList()),

            _ => incoming
        };
    }
}
