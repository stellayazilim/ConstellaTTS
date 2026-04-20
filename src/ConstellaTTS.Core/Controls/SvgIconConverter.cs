using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Svg.Skia;

namespace ConstellaTTS.Core.Controls;

/// <summary>
/// Converts an avares:// SVG path string to an <see cref="IImage"/> for use in bindings.
/// </summary>
public class SvgIconConverter : IValueConverter
{
    public static readonly SvgIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrEmpty(path))
            return null;

        var svg = new SvgImage();
        svg.Source = SvgSource.LoadFromStream(
            Avalonia.Platform.AssetLoader.Open(new Uri(path)));
        return svg;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
