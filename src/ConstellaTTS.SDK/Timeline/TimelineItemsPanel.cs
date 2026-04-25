
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using ConstellaTTS.SDK.ViewModelContracts;

namespace ConstellaTTS.SDK.Timeline;

/// <summary>
/// Custom items panel for timeline blocks.
///
/// Avalonia's stock Canvas reads <c>Canvas.Left</c> / <c>Canvas.Top</c>
/// from its direct child during arrange. When used inside an
/// <see cref="ItemsControl"/>, those direct children are wrapping
/// <see cref="ContentPresenter"/>s, not the elements declared inside
/// the DataTemplate. Setting <c>Canvas.Left</c> on the template root
/// looks fine on screen because Avalonia forwards attached property
/// values onto the container during measure, but the ContentPresenter
/// itself is still arranged at (0, 0) — so pointer hit-tests resolve
/// to whatever ContentPresenter is on top of the stack at the origin
/// rather than the visually-positioned block.
///
/// To dodge that, this panel arranges each ContentPresenter directly
/// from the DataContext it carries: it reads <c>StartSec</c> and
/// <c>DurationSec</c> from the bound <see cref="IStageViewModel"/> and
/// projects them through the global <see cref="TimelineViewport"/>.
/// Hit-test bounds end up matching what the user sees, the
/// DataTemplate keeps its declarative layout for the inner content,
/// and there's no attached-property forwarding to debug.
///
/// The panel listens to viewport changes (zoom, scroll) and to changes
/// on each child's DataContext so blocks reflow live as the user
/// drags the timeline.
/// </summary>
public sealed class TimelineItemsPanel : Panel
{
    private const double TopPadding = 4.0;
    private const double BlockHeight = 48.0;

    public TimelineItemsPanel()
    {
        // Repaint when the timeline viewport zooms or scrolls. The
        // viewport is a process-wide singleton; subscribing here keeps
        // every track row in sync without a binding fan-out per child.
        TimelineViewport.Current.PropertyChanged += (_, _) => InvalidateArrange();

        // Track DataContext-bound section / stage VMs as children
        // attach — listening for StartSec / DurationSec changes lets
        // the panel reflow when a block is moved or resized in code
        // (e.g. via a future drag-edit gesture) without forcing the
        // caller to invalidate manually.
        Children.CollectionChanged += OnChildrenChanged;
    }

    private void OnChildrenChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems is not null)
            foreach (Avalonia.Controls.Control c in e.NewItems)
                Hook(c);
        if (e.OldItems is not null)
            foreach (Avalonia.Controls.Control c in e.OldItems)
                Unhook(c);
    }

    private void Hook(Avalonia.Controls.Control child)
    {
        child.PropertyChanged += OnChildPropertyChanged;
        if (child.DataContext is System.ComponentModel.INotifyPropertyChanged vm)
            vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void Unhook(Avalonia.Controls.Control child)
    {
        child.PropertyChanged -= OnChildPropertyChanged;
        if (child.DataContext is System.ComponentModel.INotifyPropertyChanged vm)
            vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnChildPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != DataContextProperty) return;
        if (e.OldValue is System.ComponentModel.INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= OnVmPropertyChanged;
        if (e.NewValue is System.ComponentModel.INotifyPropertyChanged newVm)
            newVm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Only the geometric properties affect arrangement; ignore
        // chatter from unrelated VM properties (Label, Bg, etc.).
        if (e.PropertyName is nameof(IStageViewModel.StartSec)
                            or nameof(IStageViewModel.DurationSec))
            InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        // Each child is a ContentPresenter wrapping the DataTemplate output;
        // measure them with infinite width so DurationSec drives the size,
        // not whatever happens to be available on first layout.
        var infinite = new Size(double.PositiveInfinity, BlockHeight);
        foreach (var child in Children)
            child.Measure(infinite);

        return new Size(
            double.IsInfinity(availableSize.Width)  ? 0 : availableSize.Width,
            BlockHeight + TopPadding);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var viewport = TimelineViewport.Current;
        var pxPerSec = viewport.PxPerSec;
        var scrollSec = viewport.ScrollOffsetSec;

        foreach (var child in Children)
        {
            // The DataContext of a ContentPresenter inside ItemsControl is
            // the bound item; in our case that's IStageViewModel (covering
            // both sections and stages). Skip anything that doesn't fit
            // the contract — keeps the panel forgiving if some unrelated
            // visual sneaks in.
            if (child.DataContext is not IStageViewModel block)
            {
                child.Arrange(new Rect(0, 0, 0, 0));
                continue;
            }

            var leftPx = (block.StartSec - scrollSec) * pxPerSec;
            var widthPx = Math.Max(0, block.DurationSec * pxPerSec);

            child.Arrange(new Rect(leftPx, TopPadding, widthPx, BlockHeight));
        }

        return finalSize;
    }
}
