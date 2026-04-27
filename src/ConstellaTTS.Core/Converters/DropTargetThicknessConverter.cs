using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace ConstellaTTS.Core.Converters;

/// <summary>
/// One-way bool \u2192 <see cref="Thickness"/> converter for the
/// section template's drop-target glow border. <c>true</c> renders a
/// uniform 2px border (lit border-brush is set in XAML), <c>false</c>
/// collapses to zero so the block reads as a flat fill in its
/// resting state.
///
/// <para>
/// Avalonia doesn't ship a stock bool-to-thickness converter and the
/// inverse-bool / numeric-to-thickness converters in the framework
/// don't fit \u2014 this is small enough to live as its own static
/// instance. The two thickness values are constant rather than
/// dependency-properties because the design has no need to vary
/// glow intensity per call site.
/// </para>
/// </summary>
public sealed class DropTargetThicknessConverter : IValueConverter
{
    public static readonly DropTargetThicknessConverter Instance = new();

    /// <summary>
    /// Border thickness applied while the block is the active drop
    /// target. 2px matches the section's other accent strips
    /// (dirty-yellow on the left, emotion-gradient on the bottom)
    /// so the highlight reads as the same visual weight.
    /// </summary>
    private static readonly Thickness ActiveThickness   = new(2);

    /// <summary>
    /// Resting-state thickness \u2014 zero so the section reads
    /// flush with the block's fill, identical to the pre-drag
    /// appearance.
    /// </summary>
    private static readonly Thickness InactiveThickness = new(0);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ActiveThickness : InactiveThickness;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("DropTargetThicknessConverter is one-way.");
}
