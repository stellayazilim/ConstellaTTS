using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConstellaTTS.Core.ViewModels;
using ConstellaTTS.SDK;
using ConstellaTTS.SDK.Timeline;

namespace ConstellaTTS.Core.Views;

/// <summary>
/// Self-drawing project overview — a miniature view of every track's
/// blocks, with a semi-transparent shade over the inactive range and a
/// bright rectangle framing the current viewport. Two gestures:
///
///   • Drag inside the viewport rectangle → pan. Same semantics as
///     Shift+Wheel but with direct proportional mapping: 1 minimap px
///     moves the viewport by (TotalSec / minimapWidth) seconds.
///
///   • Drag on empty minimap space → select a range. On release the
///     viewport is fitted to that range (ScrollOffsetSec + PxPerSec
///     both change so the selection exactly fills the canvas).
///
/// Length (TotalSec) = max(project_end, 30) → always at least half a
/// minute so a freshly-created project isn't a single pixel-wide view.
/// "project_end" walks every track's Sections and takes the maximum
/// EndSec; this works for both IStageViewModel and ISectionViewModel
/// since EndSec is on the base interface.
/// </summary>
public sealed class TimelineMinimapControl : Control
{
    /// <summary>Lower bound for TotalSec so a blank project still renders sensibly.</summary>
    private const double MinTotalSec = 30;

    private readonly ITimelineViewport _viewport;

    /// <summary>Tracks whose Sections we sample. Assigned via <see cref="SetTracks"/>.</summary>
    private ObservableCollection<ITrackViewModel>? _tracks;

    // ── Drag state ───────────────────────────────────────────────────────
    private enum DragMode { None, Pan, Select }
    private DragMode _dragMode;
    private double   _dragAnchorSec;    // cursor time at press (for delta math in Pan; start of range in Select)
    private double   _dragStartScroll;  // Pan: ScrollOffsetSec snapshot at press
    private double   _dragSelectEndSec; // Select: current end while dragging

    public TimelineMinimapControl()
    {
        _viewport = TimelineViewport.Current;

        _viewport.PropertyChanged += OnViewportPropertyChanged;
        SizeChanged               += (_, _) => InvalidateVisual();

        // Gestures
        PointerPressed  += OnPressed;
        PointerMoved    += OnMoved;
        PointerReleased += OnReleased;
    }

    /// <summary>
    /// Bind the minimap to a tracks collection. Re-subscribes to each
    /// track's Sections CollectionChanged so adding/removing/modifying a
    /// block repaints. Call from the hosting view once its DataContext
    /// is ready; re-callable if the collection reference ever changes.
    /// </summary>
    public void SetTracks(ObservableCollection<ITrackViewModel>? tracks)
    {
        // Unhook from any prior collection. Per-track hooks are tracked
        // implicitly via the old reference and will be garbage-collected
        // once nothing else holds the track; for an app-lifetime singleton
        // viewport we accept that tradeoff rather than tracking handlers.
        if (_tracks is not null)
        {
            _tracks.CollectionChanged -= OnTracksCollectionChanged;
            foreach (var t in _tracks)
                UnhookTrack(t);
        }

        _tracks = tracks;

        if (_tracks is not null)
        {
            _tracks.CollectionChanged += OnTracksCollectionChanged;
            foreach (var t in _tracks)
                HookTrack(t);
        }

        InvalidateVisual();
    }

    private void HookTrack(ITrackViewModel t)
    {
        if (t.Sections is INotifyCollectionChanged incc)
            incc.CollectionChanged += OnSectionsChanged;
        // Block-level property changes (StartSec / DurationSec after
        // a bump) also need repaints. Subscribe per-block.
        foreach (var b in t.Sections)
            b.PropertyChanged += OnBlockPropertyChanged;
    }

    private void UnhookTrack(ITrackViewModel t)
    {
        if (t.Sections is INotifyCollectionChanged incc)
            incc.CollectionChanged -= OnSectionsChanged;
        foreach (var b in t.Sections)
            b.PropertyChanged -= OnBlockPropertyChanged;
    }

    private void OnTracksCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (ITrackViewModel t in e.OldItems) UnhookTrack(t);
        if (e.NewItems is not null)
            foreach (ITrackViewModel t in e.NewItems) HookTrack(t);
        InvalidateVisual();
    }

    private void OnSectionsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (IStageViewModel b in e.OldItems) b.PropertyChanged -= OnBlockPropertyChanged;
        if (e.NewItems is not null)
            foreach (IStageViewModel b in e.NewItems) b.PropertyChanged += OnBlockPropertyChanged;
        InvalidateVisual();
    }

    private void OnBlockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Any geometry change on any block re-renders. Color / label
        // changes also redraw but they're the exception so we don't
        // gate by property name.
        InvalidateVisual();
    }

    private void OnViewportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITimelineViewport.PxPerSec) ||
            e.PropertyName == nameof(ITimelineViewport.ScrollOffsetSec))
        {
            InvalidateVisual();
        }
    }

    // ── Gestures ─────────────────────────────────────────────────────────
    //
    // Modifier decides the mode — NOT where the cursor lands:
    //   • Plain        → Pan. Cursor's time becomes the viewport centre,
    //                    so click-then-drag smoothly slides the viewport
    //                    along with the cursor. Works identically whether
    //                    you press inside or outside the current viewport
    //                    rectangle — so missing the rectangle by a pixel
    //                    doesn't jump you into the wrong gesture.
    //   • Ctrl+drag    → Select-range. Paints a magenta rectangle from
    //                    press to current pointer; on release the canvas
    //                    zoom/scroll fit that exact window.
    //
    // Per-tick clamp: TotalSec includes the current viewport end so the
    // minimap always grows to contain the view. Clicking "outside" is
    // impossible unless the user scrolled with the ruler past TotalSec,
    // which PxToSec will still accept but centre-clamp to something valid.

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (Bounds.Width <= 0) return;

        var x = Math.Clamp(e.GetPosition(this).X, 0, Bounds.Width);

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Start a select-range gesture. On release we'll fit the
            // canvas to this span — this is the minimap equivalent of
            // drawing a zoom-in rectangle.
            _dragMode         = DragMode.Select;
            _dragAnchorSec    = PxToSec(x);
            _dragSelectEndSec = _dragAnchorSec;
            Cursor            = new Cursor(StandardCursorType.Cross);
        }
        else
        {
            // Plain → pan. Delta-based: snapshot where the cursor landed
            // and the scroll offset at that moment, then Move adjusts by
            // (cursorSec − pressSec). Press alone does nothing — only
            // motion translates to a scroll change, so clicking on an
            // off-screen-extended viewport rectangle won't jump the canvas.
            _dragMode        = DragMode.Pan;
            _dragAnchorSec   = PxToSec(x);
            _dragStartScroll = _viewport.ScrollOffsetSec;
            Cursor           = new Cursor(StandardCursorType.SizeAll);
        }

        InvalidateVisual();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (_dragMode == DragMode.None) return;

        var x = Math.Clamp(e.GetPosition(this).X, 0, Bounds.Width);

        if (_dragMode == DragMode.Pan)
        {
            // Translate cursor delta to a seconds delta against the press
            // snapshot. Left-clamp at 0 (users can't scroll past project
            // start); no right clamp — the canvas may reach past project
            // end so new blocks can be drawn in empty future space.
            var deltaSec = PxToSec(x) - _dragAnchorSec;
            var newScroll = _dragStartScroll + deltaSec;
            _viewport.ScrollOffsetSec = Math.Max(0, newScroll);
        }
        else // Select
        {
            _dragSelectEndSec = PxToSec(x);
            InvalidateVisual();
        }
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragMode == DragMode.Select)
        {
            var s  = Math.Min(_dragAnchorSec, _dragSelectEndSec);
            var en = Math.Max(_dragAnchorSec, _dragSelectEndSec);

            // Only fit if the user actually swept a range. A Ctrl+click
            // with no movement is treated as a cancel — nothing changes.
            if (en - s > 0.5)
                FitViewportTo(s, en);
        }

        _dragMode = DragMode.None;
        Cursor    = Cursor.Default;
        e.Pointer.Capture(null);
        InvalidateVisual();
    }

    /// <summary>
    /// Re-project the viewport so the time range (<paramref name="startSec"/>,
    /// <paramref name="endSec"/>) exactly fills the track canvas. The canvas
    /// width is inferred from the current viewport: visibleDur = Bounds
    /// .Width of the canvas / PxPerSec. Since we're inside the minimap
    /// control we don't know the canvas width directly, so we reuse
    /// (visibleDur / selectionDur) = newPxPerSec / oldPxPerSec to solve
    /// for newPxPerSec without a canvas-width reference.
    /// </summary>
    private void FitViewportTo(double startSec, double endSec)
    {
        var currentVisible = Math.Max(0.001, CurrentVisibleDur());
        var wantVisible    = Math.Max(0.1, endSec - startSec);
        var newPxPerSec    = _viewport.PxPerSec * (currentVisible / wantVisible);

        // Respect TrackListView's clamp range so zoom stays sane.
        newPxPerSec = Math.Clamp(newPxPerSec, 4, 400);

        _viewport.PxPerSec        = newPxPerSec;
        _viewport.ScrollOffsetSec = startSec;
    }

    private void CenterViewportAt(double sec)
    {
        var dur      = CurrentVisibleDur();
        var newStart = Math.Max(0, sec - dur / 2);
        _viewport.ScrollOffsetSec = newStart;
    }

    // ── Rendering ────────────────────────────────────────────────────────

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var total  = TotalSec();
        var tracks = _tracks;

        // Row geometry: reserve a 6 px top/bottom margin so consecutive
        // rows have breathing room when rendered at small heights.
        var trackCount = Math.Max(1, tracks?.Count ?? 1);
        var y0 = 6.0;
        var rowH = Math.Max(3, (h - 12) / trackCount);

        // Track bands — alternating tint so rows read distinctly.
        var bandA = new SolidColorBrush(Color.Parse("#18182A"));
        var bandB = new SolidColorBrush(Color.Parse("#15152A"));
        for (int i = 0; i < trackCount; i++)
        {
            context.FillRectangle(i % 2 == 0 ? bandA : bandB,
                new Rect(0, y0 + i * rowH, w, rowH - 1));
        }

        // Blocks.
        if (tracks is not null)
        {
            for (int ti = 0; ti < tracks.Count; ti++)
            {
                var track = tracks[ti];
                foreach (var b in track.Sections)
                {
                    var x1 = (b.StartSec / total) * w;
                    var x2 = (b.EndSec   / total) * w;
                    if (x2 <= 0 || x1 >= w) continue;
                    x1 = Math.Max(0, x1);
                    x2 = Math.Min(w, x2);

                    var bandY = y0 + ti * rowH + 1;
                    var bandH = rowH - 3;

                    var bgBrush     = new SolidColorBrush(ParseOrDefault(b.Bg, "#2A2560"));
                    var accentBrush = new SolidColorBrush(ParseOrDefault(b.AccentColor, "#7C6AF7"));

                    context.DrawRectangle(bgBrush, null,
                        new Rect(x1, bandY, Math.Max(2, x2 - x1), bandH),
                        2, 2);

                    // 2 px accent line at the top edge so tracks read even
                    // when blocks are thin slivers.
                    context.DrawRectangle(accentBrush, null,
                        new Rect(x1, bandY, Math.Max(2, x2 - x1), 2),
                        1, 1);
                }
            }
        }

        // Viewport indicator. Dim the inactive range and frame the active
        // one with a violet border — same colour family as the rest of
        // the UI's accents so the eye reads it as "current focus".
        //
        // When zoomed out or scrolled past project end, the raw viewport
        // range extends beyond [0, total]. We clamp the drawn rectangle
        // to the minimap bounds so the frame sits on the edge rather than
        // drawing off-canvas; the truncated side visually conveys "the
        // viewport extends past what the minimap represents".
        var (vs, ve) = ViewportRange();
        var vx1 = Math.Clamp((vs / total) * w, 0, w);
        var vx2 = Math.Clamp((ve / total) * w, 0, w);

        var shade = new SolidColorBrush(Color.FromArgb(0x80, 0x08, 0x08, 0x0F));
        if (vx1 > 0)     context.FillRectangle(shade, new Rect(0, 0, vx1, h));
        if (vx2 < w)     context.FillRectangle(shade, new Rect(vx2, 0, w - vx2, h));

        var frame = new Pen(new SolidColorBrush(Color.FromArgb(0xCC, 0x7C, 0x6A, 0xF7)), 1.5);
        context.DrawRectangle(null, frame, new Rect(vx1, 1, Math.Max(2, vx2 - vx1), h - 2));

        // Selection preview while the user is drag-selecting a zoom range.
        if (_dragMode == DragMode.Select)
        {
            var s = Math.Min(_dragAnchorSec, _dragSelectEndSec);
            var en = Math.Max(_dragAnchorSec, _dragSelectEndSec);
            var sx1 = Math.Max(0, (s  / total) * w);
            var sx2 = Math.Min(w, (en / total) * w);

            var selFill = new SolidColorBrush(Color.FromArgb(0x30, 0xD0, 0x60, 0xFF));
            var selPen  = new Pen(new SolidColorBrush(Color.FromArgb(0xB0, 0xD0, 0x60, 0xFF)), 1);
            context.FillRectangle(selFill, new Rect(sx1, 0, sx2 - sx1, h));
            context.DrawRectangle(null, selPen, new Rect(sx1, 0, sx2 - sx1, h));
        }
    }

    // ── Math helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// Project length used for rendering and pixel↔time math. Strictly
    /// the latest block's EndSec, floored at <see cref="MinTotalSec"/>
    /// so a blank project still renders a usable strip.
    ///
    /// This is deliberately independent of the viewport. The minimap's
    /// x-extent represents "the project" — not "the view onto the project".
    /// When the user zooms far out or scrolls past the last block, the
    /// viewport rectangle overflows the minimap's edges; Render clamps
    /// the drawn rectangle to [0, w] so the minimap stays a stable,
    /// fixed-width reference while the user navigates. A minimap whose
    /// scale drifted with zoom/scroll would move under the user's cursor
    /// between clicks, which breaks the "this is where I am in the
    /// project" reading of it.
    /// </summary>
    private double TotalSec()
    {
        double maxEnd = 0;
        if (_tracks is not null)
        {
            foreach (var t in _tracks)
                foreach (var b in t.Sections)
                    if (b.EndSec > maxEnd) maxEnd = b.EndSec;
        }
        return Math.Max(maxEnd, MinTotalSec);
    }

    /// <summary>
    /// The (start, end) range of the current track canvas viewport in
    /// seconds. End is estimated from current PxPerSec and the minimap's
    /// own width as a proxy, since the minimap doesn't have a reference
    /// to the canvas it represents.
    /// </summary>
    private (double start, double end) ViewportRange()
    {
        var start = _viewport.ScrollOffsetSec;
        var end   = start + CurrentVisibleDur();
        return (start, end);
    }

    /// <summary>
    /// Seconds currently visible in the track canvas. The minimap can't
    /// know the canvas's width, so we proxy it with our own width as
    /// both live in the same row layout and share the same X extents.
    /// For very tall/wide layouts where this breaks down, callers can
    /// inject the real width later.
    /// </summary>
    private double CurrentVisibleDur()
    {
        // Proxy: estimate canvas visible duration from the minimap's own
        // geometry. The minimap is the full width of the timeline canvas
        // below it, so minimap.Width / minimap.Width == 1 — which doesn't
        // help. Instead we use the viewport itself: PxPerSec × someWidth.
        // We pick a reasonable default of 800 px if we have nothing
        // better; first real draw/mount will correct it when the caller
        // supplies one via the hosting control.
        var w = Math.Max(100, Bounds.Width);
        return w / Math.Max(0.001, _viewport.PxPerSec);
    }

    private double PxToSec(double x)
    {
        var w = Math.Max(1, Bounds.Width);
        return (x / w) * TotalSec();
    }

    private static Color ParseOrDefault(string? hex, string fallback) =>
        Color.TryParse(hex ?? "", out var c) ? c : Color.Parse(fallback);
}
