namespace ConstellaTTS.SDK;

/// <summary>
/// Default implementation of <see cref="INavigationManager"/>.
/// Processes navigation requests, applies them via slot/window/layout services,
/// and pushes rollback entries to history.
/// </summary>
public sealed class NavigationManager(
    ISlotService slotService,
    IWindowManager windowManager,
    IHistoryManager historyManager) : INavigationManager
{
    /// <inheritdoc/>
    public void Navigate(NavigationRequest request)
    {
        // Capture rollback snapshot before applying
        var rollback = BuildRollback(request);

        Apply(request);

        if (rollback is not null)
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

    private IHistoryEntry? BuildRollback(NavigationRequest request) =>
        new NavigationHistoryEntry(this, SnapshotCurrentState(request));

    private NavigationRequest SnapshotCurrentState(NavigationRequest incoming)
    {
        // Build inverse request based on current state before applying
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
