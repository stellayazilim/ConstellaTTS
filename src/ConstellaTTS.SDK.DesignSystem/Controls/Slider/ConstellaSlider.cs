
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;

namespace ConstellaTTS.SDK.DesignSystem.Controls.Slider;

/// <summary>
/// Custom horizontal slider used across the editor in place of the Fluent
/// theme's stock Slider.
///
/// Why a bespoke control rather than restyling the built-in one:
///
///   · Fluent's Slider has a templated thumb that picks up the system
///     accent (red on Windows by default) through pseudo-classes that
///     are difficult to override cleanly.
///   · We want a track that can paint a multi-stop gradient
///     (the emotion scale) under the thumb without hacks like
///     overlaying a transparent slider on a separate gradient bar.
///   · We want the thumb's fill to optionally sample the gradient at
///     the current value, so the indicator visibly warms or cools
///     while the user drags. Doing this inside a Style.Setter on a
///     stock Slider gets ugly fast.
///
/// The control inherits <see cref="RangeBase"/> for Minimum / Maximum /
/// Value plumbing and adds:
///
///   · <see cref="Mode"/>         — Solid / Gradient / Emotion presentation.
///   · <see cref="AccentBrush"/>  — solid colour used for the thumb in
///                                  non-Emotion modes and for the filled
///                                  portion of the track in Solid mode.
///   · <see cref="TrackBrush"/>   — optional gradient for the track in
///                                  Gradient mode. Ignored in Emotion mode
///                                  (which always uses the canonical
///                                  emotion gradient resource).
///   · <see cref="ThumbBrush"/>   — read-only computed brush surfaced for
///                                  the template; Emotion mode samples
///                                  <see cref="EmotionColors"/> at the
///                                  current value, otherwise falls back
///                                  to AccentBrush.
///
/// Pointer interaction is handled directly in this control — pressing
/// or dragging anywhere on the track sets Value to the corresponding
/// position. There is no separate Thumb control to capture; the whole
/// slider acts as a single hit target which avoids the Fluent thumb's
/// 1-pixel grab zone.
/// </summary>
public class ConstellaSlider : RangeBase
{
    public static readonly StyledProperty<ConstellaSliderMode> ModeProperty =
        AvaloniaProperty.Register<ConstellaSlider, ConstellaSliderMode>(
            nameof(Mode), ConstellaSliderMode.Solid);

    public static readonly StyledProperty<IBrush?> AccentBrushProperty =
        AvaloniaProperty.Register<ConstellaSlider, IBrush?>(nameof(AccentBrush));

    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<ConstellaSlider, IBrush?>(nameof(TrackBrush));

    public static readonly DirectProperty<ConstellaSlider, IBrush?> ThumbBrushProperty =
        AvaloniaProperty.RegisterDirect<ConstellaSlider, IBrush?>(
            nameof(ThumbBrush),
            o => o.ThumbBrush);

    public static readonly DirectProperty<ConstellaSlider, double> NormalizedValueProperty =
        AvaloniaProperty.RegisterDirect<ConstellaSlider, double>(
            nameof(NormalizedValue),
            o => o.NormalizedValue);

    private IBrush? _thumbBrush;

    public ConstellaSliderMode Mode
    {
        get => GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public IBrush? AccentBrush
    {
        get => GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    public IBrush? TrackBrush
    {
        get => GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    /// <summary>
    /// Computed thumb fill — read-only, surfaced for the template binding.
    /// Refreshed whenever Value, Mode or AccentBrush changes.
    /// </summary>
    public IBrush? ThumbBrush
    {
        get => _thumbBrush;
        private set => SetAndRaise(ThumbBrushProperty, ref _thumbBrush, value);
    }

    /// <summary>
    /// Value normalised to 0..1 against [Minimum, Maximum]. Used by the
    /// template to position the thumb without forcing the template into
    /// per-frame measure passes — Avalonia animates the binding source
    /// so the thumb glides during a drag.
    /// </summary>
    public double NormalizedValue
    {
        get
        {
            var span = Maximum - Minimum;
            if (span <= 0) return 0;
            return Math.Clamp((Value - Minimum) / span, 0, 1);
        }
    }

    static ConstellaSlider()
    {
        // Reasonable defaults so a freshly-dropped slider fills its grid
        // cell horizontally and reserves enough vertical space for the
        // thumb. Callers can still override either; these just make the
        // common case look right without per-instance setters.
        HorizontalAlignmentProperty.OverrideDefaultValue<ConstellaSlider>(
            Avalonia.Layout.HorizontalAlignment.Stretch);
        MinHeightProperty.OverrideDefaultValue<ConstellaSlider>(22);
        MinWidthProperty.OverrideDefaultValue<ConstellaSlider>(80);
        FocusableProperty.OverrideDefaultValue<ConstellaSlider>(true);

        // Recompute the thumb fill when any of its inputs change. We
        // can't bind ThumbBrush in XAML because the Emotion mode case
        // depends on Value — staying in code keeps the logic in one place.
        ValueProperty.Changed.AddClassHandler<ConstellaSlider>((s, _) => s.RecomputeThumbBrush());
        ModeProperty.Changed.AddClassHandler<ConstellaSlider>((s, _) => s.RecomputeThumbBrush());
        AccentBrushProperty.Changed.AddClassHandler<ConstellaSlider>((s, _) => s.RecomputeThumbBrush());
        MinimumProperty.Changed.AddClassHandler<ConstellaSlider>((s, _) =>
        {
            s.RecomputeThumbBrush();
            s.RaiseNormalizedChanged();
        });
        MaximumProperty.Changed.AddClassHandler<ConstellaSlider>((s, _) =>
        {
            s.RecomputeThumbBrush();
            s.RaiseNormalizedChanged();
        });
        ValueProperty.Changed.AddClassHandler<ConstellaSlider>((s, _) => s.RaiseNormalizedChanged());
    }

    public ConstellaSlider()
    {
        RecomputeThumbBrush();
    }

    private void RecomputeThumbBrush()
    {
        if (Mode == ConstellaSliderMode.Emotion)
        {
            var colour = EmotionColors.Sample(Value, Minimum, Maximum);
            ThumbBrush = new SolidColorBrush(colour);
            return;
        }
        ThumbBrush = AccentBrush;
    }

    private void RaiseNormalizedChanged()
    {
        RaisePropertyChanged(NormalizedValueProperty, default, NormalizedValue);
        UpdateThumbAndFillLayout();
    }

    // ── Pointer interaction ─────────────────────────────────────────────
    //
    // The whole control is the hit target. Press or drag anywhere along
    // the track and Value snaps to the matching position. This avoids
    // the Fluent slider's narrow thumb hit zone — pressing 5 pixels off
    // the thumb still grabs it.

    private bool _isDragging;

    private Border? _track;
    private Border? _fill;
    private Avalonia.Controls.Shapes.Ellipse? _thumb;

    /// <summary>
    /// Hook the named template parts. The track + fill + thumb are
    /// updated imperatively when NormalizedValue changes — binding
    /// ColumnDefinition.Width or a Grid star factor to a double in
    /// pure XAML needs a converter; this is shorter and avoids the
    /// per-frame layout pass that a Grid star recalc would trigger.
    /// </summary>
    protected override void OnApplyTemplate(
        Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        _track = e.NameScope.Find<Border>("PART_Track");
        _fill  = e.NameScope.Find<Border>("PART_Fill");
        // Find by the concrete type — NameScope.Find<T>() requires an
        // exact match, so a bare Shape lookup misses the Ellipse the
        // template actually contains and the thumb stays at margin 0.
        _thumb = e.NameScope.Find<Avalonia.Controls.Shapes.Ellipse>("PART_Thumb");

        UpdateThumbAndFillLayout();
    }

    private void UpdateThumbAndFillLayout()
    {
        if (_track is null) return;
        var trackWidth = _track.Bounds.Width;
        if (trackWidth <= 0) return;

        var x = NormalizedValue * trackWidth;

        if (_fill is not null)
            _fill.Width = x;

        if (_thumb is not null)
        {
            // Centre the thumb on the value position by offsetting half
            // its own width to the left.
            var thumbHalf = _thumb.Width / 2;
            _thumb.Margin = new Thickness(x - thumbHalf, 0, 0, 0);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;

        _isDragging = true;
        e.Pointer.Capture(this);
        SetValueFromPointer(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_isDragging) return;
        SetValueFromPointer(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_isDragging) return;

        _isDragging = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _isDragging = false;
    }

    private void SetValueFromPointer(Point pos)
    {
        var width = _track?.Bounds.Width ?? Bounds.Width;
        if (width <= 0) return;

        // Translate the pointer into track-local coordinates if the track
        // is offset from the control's origin (e.g. when padding is applied).
        var trackOrigin = _track?.TranslatePoint(new Point(0, 0), this) ?? new Point(0, 0);
        var localX = pos.X - trackOrigin.X;

        var t = Math.Clamp(localX / width, 0.0, 1.0);
        var span = Maximum - Minimum;
        var newValue = Minimum + (t * span);

        // Honour SmallChange as the snap step if it's positive — gives
        // temperature sliders integer-decimal feel without rounding here.
        if (SmallChange > 0)
        {
            newValue = Math.Round(newValue / SmallChange) * SmallChange;
            newValue = Math.Clamp(newValue, Minimum, Maximum);
        }
        Value = newValue;
    }

    /// <summary>
    /// React to size changes — if the control is resized the thumb's
    /// pixel position has to be recomputed even though Value didn't
    /// change. Cheaper than re-binding inside the template.
    /// </summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        var size = base.ArrangeOverride(finalSize);
        UpdateThumbAndFillLayout();
        return size;
    }
}
