

namespace ConstellaTTS.SDK.DesignSystem.Controls.Slider;

/// <summary>
/// Visual presentation modes for <see cref="ConstellaSlider"/>.
///
/// Each mode picks one of three behaviours for the track and thumb:
///
///   · <see cref="Solid"/>     — single accent colour for both, parameterised
///                               by <see cref="ConstellaSlider.AccentBrush"/>.
///                               The default; matches plain numeric sliders
///                               like temperature.
///   · <see cref="Gradient"/>  — track painted with a multi-stop gradient brush
///                               supplied via <see cref="ConstellaSlider.TrackBrush"/>.
///                               Thumb stays solid (AccentBrush) and rides over
///                               the gradient.
///   · <see cref="Emotion"/>   — like Gradient but the thumb's fill is sampled
///                               from the cool-to-hot emotion scale at the
///                               current value, so dragging the slider visibly
///                               warms or cools the indicator. The control
///                               applies the canonical emotion gradient
///                               automatically — callers don't need to pass
///                               their own TrackBrush.
/// </summary>
public enum ConstellaSliderMode
{
    Solid,
    Gradient,
    Emotion,
}
