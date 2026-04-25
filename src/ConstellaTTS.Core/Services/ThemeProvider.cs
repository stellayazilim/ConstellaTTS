using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using ConstellaTTS.Core.Converters;
using ConstellaTTS.Core.Misc.Theme;
using ConstellaTTS.SDK.Theme;

namespace ConstellaTTS.Core.Services;

/// <summary>
/// IThemeProvider implementation.
///
/// Supports three registration paths:
///   RegisterGlobal    — adds to Application.Styles or Application.Resources; always active.
///   RegisterForTheme  — wraps resources in ThemeDictionaries[ThemeVariant]; Avalonia
///                       activates them automatically when RequestedThemeVariant matches.
///   LoadColorTheme /
///   ApplyTheme        — parses a JSON color theme file and writes resolved brushes
///                       directly into Application.Resources.
/// </summary>
public sealed class ThemeProvider : IThemeProvider
{
    private readonly Application _app;
    private ThemeFile? _loadedFile;
    private string _currentTheme;

    /// <inheritdoc/>
    public string CurrentTheme => _currentTheme;

    /// <inheritdoc/>
    public event EventHandler<string>? ThemeChanged;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new ThemeValueConverter() }
    };

    public ThemeProvider(Application app)
    {
        _app = app;
        _currentTheme = app.RequestedThemeVariant?.Key?.ToString() ?? "Dark";
    }

    // Global registration

    /// <inheritdoc/>
    public void RegisterGlobal(IStyle styles) =>
        _app.Styles.Add(styles);

    /// <inheritdoc/>
    public void RegisterGlobal(ResourceDictionary resources) =>
        _app.Resources.MergedDictionaries.Add(resources);

    // Per-theme registration

    /// <inheritdoc/>
    public void RegisterForTheme(string themeKey, ResourceDictionary resources)
    {
        var wrapper = new ResourceDictionary();
        wrapper.ThemeDictionaries[ToThemeVariant(themeKey)] = resources;
        _app.Resources.MergedDictionaries.Add(wrapper);
    }

    // Color theme

    /// <inheritdoc/>
    public void LoadColorTheme(string path)
    {
        var json = File.ReadAllText(path);
        _loadedFile = JsonSerializer.Deserialize<ThemeFile>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Failed to parse color theme file: {path}");
    }

    /// <inheritdoc/>
    public void ApplyTheme(string themeId)
    {
        if (_loadedFile is null)
            throw new InvalidOperationException("No color theme loaded. Call LoadColorTheme first.");

        if (!_loadedFile.Themes.TryGetValue(themeId, out var theme))
            throw new KeyNotFoundException($"Theme '{themeId}' not found in the loaded file.");

        foreach (var (conventionName, themeValue) in theme)
        {
            var brush = ResolveBrush(themeValue, _loadedFile.Tokens);
            if (brush is not null)
                _app.Resources[conventionName] = brush;
        }

        _currentTheme = themeId;
        _app.RequestedThemeVariant = ToThemeVariant(themeId);
        ThemeChanged?.Invoke(this, themeId);
    }

    // Helpers

    private static ThemeVariant ToThemeVariant(string key) =>
        key.ToLowerInvariant() switch
        {
            "light"  => ThemeVariant.Light,
            "system" => ThemeVariant.Default,
            "dark"   => ThemeVariant.Dark,
            _        => new ThemeVariant(key, ThemeVariant.Dark)
        };

    private static IBrush? ResolveBrush(ThemeValue value, Dictionary<string, BrushValue> tokens)
    {
        if (value.IsReference)
        {
            var reference = value.Reference!;
            if (tokens.TryGetValue(reference, out var tokenBrush))
                return BuildBrush(tokenBrush);
            if (Color.TryParse(reference, out var color))
                return new SolidColorBrush(color);
            return null;
        }

        return value.Brush is not null ? BuildBrush(value.Brush) : null;
    }

    private static IBrush? BuildBrush(BrushValue v)
    {
        if (!v.IsValid) return null;

        if (v.Solid is not null)
            return Color.TryParse(v.Solid, out var c) ? new SolidColorBrush(c) : null;

        if (v.Linear is not null) return BuildLinearGradient(v.Linear);
        if (v.Radial is not null) return BuildRadialGradient(v.Radial);

        return null;
    }

    private static LinearGradientBrush BuildLinearGradient(string[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint   = new RelativePoint(1, 0, RelativeUnit.Relative)
        };

        for (int i = 0; i < stops.Length; i++)
        {
            if (!Color.TryParse(stops[i], out var color)) continue;
            brush.GradientStops.Add(new GradientStop(color,
                stops.Length == 1 ? 0.0 : (double)i / (stops.Length - 1)));
        }

        return brush;
    }

    private static RadialGradientBrush BuildRadialGradient(string[] stops)
    {
        var brush = new RadialGradientBrush();

        for (int i = 0; i < stops.Length; i++)
        {
            if (!Color.TryParse(stops[i], out var color)) continue;
            brush.GradientStops.Add(new GradientStop(color,
                stops.Length == 1 ? 0.0 : (double)i / (stops.Length - 1)));
        }

        return brush;
    }
}
