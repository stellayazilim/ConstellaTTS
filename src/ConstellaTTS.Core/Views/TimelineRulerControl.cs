using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using ConstellaTTS.SDK.Timeline;

namespace ConstellaTTS.Core.Views;

/// <summary>
/// Self-drawing timeline ruler. Observes <see cref="TimelineViewport.Current"/>
/// and repaints whenever zoom (<c>PxPerSec</c>) or scroll
/// (<c>ScrollOffsetSec</c>) change. Tick spacing is chosen from a "nice
/// numbers" ladder so labels stay readable across zoom levels — at deep
/// zoom-in ticks might read "0.1s / 0.2s / 0.3s", at deep zoom-out
/// "30s / 1m / 1m30s".
///
/// Rendering is <c>Control.Render</c>-based rather than ItemsControl-based:
/// ticks are trivial geometry, and redrawing every zoom event via
/// <see cref="Visual.InvalidateVisual"/> is much cheaper than churning
/// control instances.
///
/// The control does NOT own the left 200 px "SECTIONS" header; the
/// hosting layout places this control in the track-area column only.
/// </summary>
public sealed class TimelineRulerControl : Control
{
    /// <summary>Candidate tick intervals in seconds, ascending.</summary>
    private static readonly double[] TickLadder =
    [
        0.05, 0.1, 0.2, 0.5,
        1,    2,   5,   10,
        20,   30,  60,  120, 300, 600, 1200, 3600
    ];

    /// <summary>Minimum pixel spacing between major ticks. Below this we bump up a rung.</summary>
    private const double MinTickSpacingPx = 60;

    // Wheel tuning — mirrors TrackListView's values so the scroll feel
    // is identical whether the user rolls the wheel over the ruler or
    // over the track canvas.
    private const double ScrollStepSec     = 2;
    private const double ZoomFactorPerStep = 1.15;
    private const double MinPxPerSec       = 4;
    private const double MaxPxPerSec       = 400;

    private readonly ITimelineViewport _viewport;

    public TimelineRulerControl()
    {
        _viewport = TimelineViewport.Current;

        // Re-render whenever the viewport's zoom or scroll changes.
        // Weak subscription isn't necessary — the viewport is a process-
        // wide singleton and the control's lifetime is dominated by the
        // window's.
        _viewport.PropertyChanged += OnViewportPropertyChanged;

        // Invalidate on resize too — tick set depends on visible width.
        SizeChanged += (_, _) => InvalidateVisual();

        // Scroll & zoom directly over the ruler. Unmodified wheel scrolls
        // horizontally (the ruler doesn't vertically scroll anything, so
        // free that gesture up); Ctrl zooms around the cursor; Shift is
        // treated the same as unmodified for muscle-memory consistency
        // with the track canvas.
        PointerWheelChanged += OnWheel;
    }

    private void OnViewportPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ITimelineViewport.PxPerSec) ||
            e.PropertyName == nameof(ITimelineViewport.ScrollOffsetSec))
        {
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Wheel handler for the ruler. The ruler is "infinite to the right"
    /// and clamped at 0 on the left — scrolling past zero is a no-op so
    /// the user can't accidentally walk off the start of the project.
    /// </summary>
    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        var delta = e.Delta.Y;
        if (delta == 0) return;

        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            // Zoom anchored at the cursor's time — matches TrackListView's feel.
            var cursorX     = e.GetPosition(this).X;
            var timeAtCursor = _viewport.PxToTime(cursorX);

            var factor      = delta > 0 ? ZoomFactorPerStep : 1.0 / ZoomFactorPerStep;
            var newPxPerSec = Math.Clamp(_viewport.PxPerSec * factor, MinPxPerSec, MaxPxPerSec);

            var newScroll = Math.Max(0, timeAtCursor - (cursorX / newPxPerSec));

            _viewport.PxPerSec        = newPxPerSec;
            _viewport.ScrollOffsetSec = newScroll;
        }
        else
        {
            // Scroll. Positive delta.Y == wheel-up == earlier in time.
            var newOffset = _viewport.ScrollOffsetSec - (delta * ScrollStepSec);
            _viewport.ScrollOffsetSec = Math.Max(0, newOffset);
        }

        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var width  = Bounds.Width;
        var height = Bounds.Height;
        if (width <= 0 || height <= 0) return;

        var pxPerSec = _viewport.PxPerSec;
        if (pxPerSec <= 0) return;

        var scrollSec = _viewport.ScrollOffsetSec;
        var endSec    = scrollSec + (width / pxPerSec);

        var tickSec = ChooseTickInterval(pxPerSec);

        // Snap the first tick to a tickSec boundary so labels always sit
        // on nice numbers (1s, 2s, 5s …) rather than scroll-aligned.
        var firstTick = Math.Ceiling(scrollSec / tickSec) * tickSec;

        var tickPen      = new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x9A, 0x93, 0xC2)), 1);
        var labelBrush   = new SolidColorBrush(Color.FromArgb(0xE0, 0xB4, 0xAC, 0xD8));
        var typeface     = new Typeface("Segoe UI");

        for (var t = firstTick; t <= endSec + 0.0001; t += tickSec)
        {
            var x = (t - scrollSec) * pxPerSec;

            // Tick line — 8px vertical at top.
            context.DrawLine(tickPen,
                             new Point(x, height - 8),
                             new Point(x, height));

            // Label — floored to a sensible precision for the chosen interval.
            var label = FormatTickLabel(t, tickSec);
            var text  = new FormattedText(
                label,
                System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                10,
                labelBrush);

            context.DrawText(text, new Point(x + 4, 6));
        }
    }

    /// <summary>
    /// Pick the smallest entry from <see cref="TickLadder"/> whose pixel
    /// width at the current zoom is ≥ <see cref="MinTickSpacingPx"/>.
    /// Falls back to the largest rung if even that is too cramped
    /// (deep zoom-out).
    /// </summary>
    private static double ChooseTickInterval(double pxPerSec)
    {
        foreach (var candidate in TickLadder)
        {
            if (candidate * pxPerSec >= MinTickSpacingPx)
                return candidate;
        }
        return TickLadder[^1];
    }

    /// <summary>
    /// Render a tick value as a human-readable label. Sub-second intervals
    /// show decimals, minute+ intervals switch to m/s notation for brevity.
    /// </summary>
    private static string FormatTickLabel(double seconds, double tickSec)
    {
        if (seconds < 0) seconds = 0;

        if (tickSec >= 60)
        {
            var totalMin = (int)Math.Floor(seconds / 60);
            var remSec   = (int)Math.Round(seconds - totalMin * 60);
            if (remSec == 60) { totalMin++; remSec = 0; }
            return remSec == 0 ? $"{totalMin}m" : $"{totalMin}m{remSec:00}s";
        }

        if (tickSec >= 1)
            return $"{seconds:0}s";

        // Sub-second: show one decimal for 0.5/0.2/0.1, two for 0.05.
        var decimals = tickSec >= 0.1 ? 1 : 2;
        return seconds.ToString($"F{decimals}") + "s";
    }
}
