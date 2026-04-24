using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;

namespace ConstellaTTS.Core.UI.Timeline;

/// <summary>
/// Pair of MultiBinding converters that project time-domain block data
/// into canvas pixel coordinates. Instances are held as static readonly
/// fields on <see cref="TimelineConverters"/> so XAML can reference
/// them via <c>{x:Static}</c> without a resource dictionary.
///
/// Bindings use these in the section/stage DataTemplates:
///
///   Canvas.Left  ← (StartSec, viewport.PxPerSec, viewport.ScrollOffsetSec)
///                  → (StartSec - ScrollOffsetSec) × PxPerSec
///
///   Width        ← (DurationSec, viewport.PxPerSec)
///                  → DurationSec × PxPerSec
///
/// When the viewport's PxPerSec or ScrollOffsetSec changes, every
/// binding re-projects automatically — zoom and scroll just work.
/// </summary>
public static class TimelineConverters
{
    /// <summary>Projects (startSec, pxPerSec, scrollOffsetSec) → Canvas.Left pixels.</summary>
    public static readonly IMultiValueConverter TimeToPx      = new TimeToPxConverter();

    /// <summary>Projects (durationSec, pxPerSec) → Width pixels.</summary>
    public static readonly IMultiValueConverter DurationToPx  = new DurationToPxConverter();

    private sealed class TimeToPxConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 3)                        return 0d;
            if (values[0] is not double startSec)        return 0d;
            if (values[1] is not double pxPerSec)        return 0d;
            if (values[2] is not double scrollOffsetSec) return 0d;

            return (startSec - scrollOffsetSec) * pxPerSec;
        }
    }

    private sealed class DurationToPxConverter : IMultiValueConverter
    {
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count < 2)                  return 0d;
            if (values[0] is not double durationSec) return 0d;
            if (values[1] is not double pxPerSec)    return 0d;

            return Math.Max(0d, durationSec * pxPerSec);
        }
    }
}
