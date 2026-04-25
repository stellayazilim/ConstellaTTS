
using Avalonia.Media;

namespace ConstellaTTS.SDK.DesignSystem.Controls.Slider;

/// <summary>
/// Colour sampling utility for the emotion scale.
///
/// The emotion gradient is anchored at five stops (0, 25, 50, 75, 100)
/// matching the <c>Emotion-N</c> tokens in <c>Tokens.axaml</c>. Each stop
/// is a calibrated cool→hot shade — blue → green → amber → orange → red.
/// <see cref="Sample"/> linearly interpolates between adjacent stops in
/// sRGB space (the gradient is short enough that perceptual-space
/// interpolation isn't worth the extra cost), so a slider thumb whose
/// fill comes from this method will smoothly track the gradient drawn
/// behind it.
///
/// The stop colours MUST stay in sync with Tokens.axaml. They're inlined
/// here rather than read from <c>Application.Current.Resources</c> so the
/// helper stays usable from non-XAML callers (tests, custom drawing) and
/// to avoid a per-call dictionary lookup on every pointer move.
/// </summary>
public static class EmotionColors
{
    private static readonly (double Stop, Color Color)[] Stops =
    [
        (0.00, Color.Parse("#60a5fa")),  // Emotion-0   — cool blue
        (0.25, Color.Parse("#34d399")),  // Emotion-25  — calm green
        (0.50, Color.Parse("#fbbf24")),  // Emotion-50  — neutral amber
        (0.75, Color.Parse("#f97316")),  // Emotion-75  — warm orange
        (1.00, Color.Parse("#ef4444")),  // Emotion-100 — hot red
    ];

    /// <summary>
    /// Sample the emotion gradient at <paramref name="t"/>, where t is
    /// normalised to 0..1 (clamped). Returns the interpolated colour
    /// between the two surrounding stops.
    /// </summary>
    public static Color Sample(double t)
    {
        t = Math.Clamp(t, 0.0, 1.0);

        // Find the segment [a, b] containing t. With only five stops, a
        // linear scan is faster than binary search.
        for (int i = 0; i < Stops.Length - 1; i++)
        {
            var a = Stops[i];
            var b = Stops[i + 1];
            if (t <= b.Stop)
            {
                var local = (t - a.Stop) / (b.Stop - a.Stop);
                return Lerp(a.Color, b.Color, local);
            }
        }
        return Stops[^1].Color;
    }

    /// <summary>
    /// Convenience overload accepting a value/range pair, e.g. emotion in
    /// 0..100. Avoids the caller having to normalise inline.
    /// </summary>
    public static Color Sample(double value, double min, double max)
    {
        var span = max - min;
        if (span <= 0) return Stops[0].Color;
        return Sample((value - min) / span);
    }

    private static Color Lerp(Color a, Color b, double t)
    {
        byte L(byte ax, byte bx) => (byte)Math.Round(ax + ((bx - ax) * t));
        return Color.FromArgb(L(a.A, b.A), L(a.R, b.R), L(a.G, b.G), L(a.B, b.B));
    }
}
