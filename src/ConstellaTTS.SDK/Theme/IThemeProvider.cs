using Avalonia.Controls;
using Avalonia.Styling;

namespace ConstellaTTS.SDK.Theme;

/// <summary>
/// Manages style and resource registration and JSON-based color theme application.
///
/// Responsibilities:
///   1. Global registration  — theme-agnostic styles and resources, always active.
///   2. Per-theme registration — resources tied to a specific theme variant via
///      Avalonia's ThemeDictionaries mechanism; activated automatically when
///      RequestedThemeVariant matches.
///   3. Color theme loading  — parses a JSON file and writes resolved brushes
///      into Application.Resources, overriding existing convention tokens.
/// </summary>
public interface IThemeProvider
{
    /// <summary>The currently active theme ID ("Dark", "Light", "System", or a custom value).</summary>
    string CurrentTheme { get; }

    // Global registration

    /// <summary>
    /// Adds a theme-agnostic style. Appended to Application.Styles and always active
    /// regardless of the current theme.
    /// </summary>
    void RegisterGlobal(IStyle styles);

    /// <summary>
    /// Adds a theme-agnostic resource dictionary. Merged into
    /// Application.Resources.MergedDictionaries and always active.
    /// </summary>
    void RegisterGlobal(ResourceDictionary resources);

    // Per-theme registration

    /// <summary>
    /// Registers a resource dictionary for a specific theme variant.
    /// Wrapped in a ThemeDictionaries entry keyed by the resolved ThemeVariant —
    /// Avalonia activates it automatically when RequestedThemeVariant matches.
    /// </summary>
    void RegisterForTheme(string themeKey, ResourceDictionary resources);

    // Color theme

    /// <summary>
    /// Parses a JSON color theme file. The file defines palette tokens and named themes
    /// that map convention names to brush values or token references.
    /// Does not modify any resources until ApplyTheme is called.
    /// </summary>
    void LoadColorTheme(string path);

    /// <summary>
    /// Activates a named theme from the loaded color theme file.
    /// Resolves all token references, builds brushes, and writes them into
    /// Application.Resources. Also updates RequestedThemeVariant.
    /// </summary>
    void ApplyTheme(string themeId);

    /// <summary>Raised after a theme is successfully applied.</summary>
    event EventHandler<string> ThemeChanged;
}
