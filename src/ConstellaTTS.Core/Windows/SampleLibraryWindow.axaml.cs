using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConstellaTTS.Core.Layout;
using ConstellaTTS.Core.ViewModels;
using ConstellaTTS.Core.Views;

namespace ConstellaTTS.Core.Windows;

public partial class SampleLibraryWindow : Window
{
    private const int FlyoutWidth    = 300;
    private const int FlyoutPadding  = 12;
    private const int MinFlyoutWidth = 260;

    private Window?  _owner;
    private Control? _timelineRegion;
    private int      _dragSuspendCount;

    private readonly Lazy<MainWindow> _mainWindow;

    public event EventHandler<bool>? VisibilityChanged;

    public SampleLibraryWindow(
        Lazy<MainWindow>       mainWindow,
        SampleLibraryView      view,
        SampleLibraryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        _mainWindow = mainWindow;

        if (this.FindControl<ContentControl>("ContentSlot") is { } slot)
        {
            view.DataContext = vm;
            slot.Content     = view;
        }
    }

    public void Toggle()
    {
        if (IsVisible) FlyoutHide();
        else           FlyoutShow();
    }

    public void FlyoutShow()
    {
        EnsureOwner();
        if (IsVisible || _owner is null) return;
        Reposition();
        base.Show(_owner);
        InstallOwnerHandlers();
        VisibilityChanged?.Invoke(this, true);
    }

    public void FlyoutHide()
    {
        if (!IsVisible) return;
        UninstallOwnerHandlers();
        base.Hide();
        VisibilityChanged?.Invoke(this, false);
    }

    public void BeginDragSuspend() => _dragSuspendCount++;
    public void EndDragSuspend()   => _dragSuspendCount = Math.Max(0, _dragSuspendCount - 1);

    private void EnsureOwner()
    {
        if (_owner is not null) return;
        _owner          = _mainWindow.Value;
        _timelineRegion = FindTimelineRegion(_owner);
    }

    private static Control? FindTimelineRegion(Window owner) =>
        owner.GetVisualDescendants()
             .OfType<MainLayout>()
             .FirstOrDefault()
             ?.FindControl<Control>("TimelineRegion");

    private void InstallOwnerHandlers()
    {
        if (_owner is null) return;
        _owner.PositionChanged += OnOwnerPositionChanged;
        _owner.PropertyChanged += OnOwnerPropertyChanged;
        _owner.Closing         += OnOwnerClosing;
        _owner.AddHandler(InputElement.PointerPressedEvent,
            OnOwnerPointerPressed, RoutingStrategies.Tunnel);
        if (_timelineRegion is not null)
            _timelineRegion.PropertyChanged += OnTimelinePropertyChanged;
    }

    private void UninstallOwnerHandlers()
    {
        if (_owner is null) return;
        _owner.PositionChanged -= OnOwnerPositionChanged;
        _owner.PropertyChanged -= OnOwnerPropertyChanged;
        _owner.Closing         -= OnOwnerClosing;
        _owner.RemoveHandler(InputElement.PointerPressedEvent, OnOwnerPointerPressed);
        if (_timelineRegion is not null)
            _timelineRegion.PropertyChanged -= OnTimelinePropertyChanged;
    }

    private void OnOwnerPositionChanged(object? s, PixelPointEventArgs e) => Reposition();
    private void OnOwnerPropertyChanged(object? s, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty) Reposition();
    }
    private void OnTimelinePropertyChanged(object? s, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty) Reposition();
    }
    private void OnOwnerClosing(object? s, WindowClosingEventArgs e) => FlyoutHide();

    private void OnOwnerPointerPressed(object? s, PointerPressedEventArgs e)
    {
        if (!IsVisible || _dragSuspendCount > 0) return;
        if (e.Source is Visual src && FindAncestorByName(src, "SampleLibraryButton")) return;

        var screenPt   = _owner!.PointToScreen(e.GetPosition(_owner));
        var flyoutRect = new PixelRect(Position,
            PixelSize.FromSize(ClientSize, DesktopScaling));

        if (!flyoutRect.Contains(screenPt)) FlyoutHide();
    }

    private void Reposition()
    {
        if (_owner is null || _timelineRegion is null) return;

        if (_timelineRegion.Bounds.Width < 1 || _timelineRegion.Bounds.Height < 1)
        {
            Dispatcher.UIThread.Post(Reposition, DispatcherPriority.Loaded);
            return;
        }

        var tl      = _timelineRegion.PointToScreen(new Point(0, 0));
        var br      = _timelineRegion.PointToScreen(
            new Point(_timelineRegion.Bounds.Width, _timelineRegion.Bounds.Height));
        var scaling = _owner.DesktopScaling;
        var pad     = (int)(FlyoutPadding * scaling);

        var widthPx  = Math.Max((int)(MinFlyoutWidth * scaling),
                           Math.Min((int)(FlyoutWidth * scaling), br.X - tl.X - pad * 2));
        var heightPx = Math.Max(pad * 4, br.Y - tl.Y - pad * 2);

        Width    = widthPx  / scaling;
        Height   = heightPx / scaling;
        Position = new PixelPoint(br.X - widthPx - pad, tl.Y + pad);
    }

    private static bool FindAncestorByName(Visual start, string name)
    {
        for (Visual? cur = start; cur is not null; cur = cur.GetVisualParent())
            if (cur is Control { Name: var n } && n == name) return true;
        return false;
    }
}
