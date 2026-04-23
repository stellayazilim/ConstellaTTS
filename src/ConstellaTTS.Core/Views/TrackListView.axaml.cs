using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using ConstellaTTS.Core.ViewModels;

namespace ConstellaTTS.Core.Views;

/// <summary>
/// Track list with manual drag-drop via pointer events.
/// Avoids Avalonia DragDrop API which changed in 12.x.
/// </summary>
public partial class TrackListView : UserControl
{
    private const int RowHeight = 56;

    private TrackViewModel? _dragging;
    private bool            _isDragging;
    private Point           _dragStart;

    public TrackListView()
    {
        InitializeComponent();
        Setup();
    }

    private TrackListViewModel? Vm => DataContext as TrackListViewModel;

    private void Setup()
    {
        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null) return;

    
    }

    // ── Pointer events ───────────────────────────────────────────────────

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        var items = this.FindControl<ItemsControl>("TracksControl");
        if (items is null) return;

        var pos = e.GetPosition(items);
        var vm  = HitTest(pos);
        if (vm is null) return;

        _dragging   = vm;
        _dragStart  = pos;
        _isDragging = false;

        e.Pointer.Capture(items);
        e.Handled = true;
    }



    // ── Helpers ──────────────────────────────────────────────────────────

    private void Reset()
    {
        _dragging   = null;
        _isDragging = false;
    }

    private static int IndexFromY(double y, int count) =>
        (int)Math.Clamp(Math.Round(y / RowHeight), 0, count);



    private TrackViewModel? HitTest(Point posInItems)
    {
        var idx    = (int)(posInItems.Y / RowHeight);
        var tracks = Vm?.Tracks;
        if (tracks is null || idx < 0 || idx >= tracks.Count) return null;
        return tracks[idx];
    }
}
