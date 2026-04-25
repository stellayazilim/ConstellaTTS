
using Avalonia.Threading;
using ConstellaTTS.Core.Actions;
using ConstellaTTS.Core.Misc.Logging;
using ConstellaTTS.SDK.History;
using ConstellaTTS.SDK.Timeline;
using Microsoft.Extensions.Logging;

namespace ConstellaTTS.Core.Misc;

/// <summary>
/// Default <see cref="IViewportHistoryRecorder"/> backed by a
/// <see cref="DispatcherTimer"/>. Funnels viewport mutations from any
/// input source (track canvas wheel, minimap drag, future keyboard
/// shortcuts) through one debounced session so the user's navigation
/// history reads as a sequence of discrete pauses rather than a stream
/// of one-tick entries.
///
/// <para>
/// <b>Lifetime.</b> Singleton — registered for the application's
/// lifetime in DI. The settle timer ticks on the UI thread regardless
/// of which view is active, so input sources can detach and re-attach
/// (project reload, tab switch) without losing pending sessions; they
/// just need to remember to <see cref="Flush"/> when they're being
/// torn down.
/// </para>
///
/// <para>
/// <b>No-op filtering.</b> Wheel ticks at the scroll-offset clamp
/// boundary (e.g. trying to scroll past time 0) register as Touches
/// but don't actually change viewport state. The settle path compares
/// FROM vs. TO and skips the push when nothing moved, so the undo
/// stack doesn't fill with empty navigation entries during a clamp
/// dance.
/// </para>
/// </summary>
public sealed class ViewportHistoryRecorder : IViewportHistoryRecorder
{
    /// <summary>
    /// Quiet threshold after which a burst is considered finished and
    /// the session pushes its entry. 250 ms is short enough that the
    /// undo entry feels like it lands "right after" the user stops
    /// scrolling, and long enough to merge a normal wheel-burst into
    /// a single session — wheel ticks during a sustained scroll
    /// usually arrive 30–60 ms apart.
    /// </summary>
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(250);

    private readonly ITimelineViewport _viewport;
    private readonly IHistoryManager   _history;
    private readonly ILogger           _log;

    private readonly DispatcherTimer _timer;

    private bool   _sessionActive;
    private double _fromPxPerSec;
    private double _fromScrollOffsetSec;

    public ViewportHistoryRecorder(
        ITimelineViewport viewport,
        IHistoryManager   history,
        ILoggerFactory    loggerFactory)
    {
        _viewport = viewport;
        _history  = history;
        _log      = loggerFactory.CreateLogger(LogCategory.WindowProcess);

        _timer = new DispatcherTimer { Interval = SettleDelay };
        _timer.Tick += (_, _) => Settle();
    }

    public void Touch()
    {
        if (!_sessionActive)
        {
            _sessionActive       = true;
            _fromPxPerSec        = _viewport.PxPerSec;
            _fromScrollOffsetSec = _viewport.ScrollOffsetSec;
        }

        // Restart the settle timer on every Touch — a sustained burst
        // keeps the session alive, a quiet pause closes it.
        _timer.Stop();
        _timer.Start();
    }

    public void Flush()
    {
        if (!_sessionActive) return;
        _timer.Stop();
        Settle();
    }

    private void Settle()
    {
        _timer.Stop();
        if (!_sessionActive) return;

        var toPxPerSec        = _viewport.PxPerSec;
        var toScrollOffsetSec = _viewport.ScrollOffsetSec;

        _sessionActive = false;

        // Filter out clamp-only bursts (e.g. user scrolled into the
        // ScrollOffsetSec=0 wall and the wheel ticks didn't actually
        // move anything). Pushing a no-op SelectAction-style entry
        // would just clutter the undo stack.
        if (toPxPerSec == _fromPxPerSec && toScrollOffsetSec == _fromScrollOffsetSec)
            return;

        // The viewport is already at TO from live input mutations; we
        // push a ViewportChangeAction whose Execute would re-apply TO
        // (no-op now, used on redo). The history manager only records;
        // it doesn't re-execute Push'd entries.
        var action = new ViewportChangeAction(_viewport,
            _fromPxPerSec, _fromScrollOffsetSec,
            toPxPerSec, toScrollOffsetSec);
        _history.Push(action);

        _log.LogDebug(
            "[VIEWPORT-COMMIT] from(zoom={Fz:F2},scroll={Fs:F2}) → to(zoom={Tz:F2},scroll={Ts:F2})",
            _fromPxPerSec, _fromScrollOffsetSec,
            toPxPerSec, toScrollOffsetSec);
    }
}
