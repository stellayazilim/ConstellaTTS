using System.Text.Json.Serialization;

namespace ConstellaTTS.Core.Theme;

/// <summary>
/// Root model for a Constella color theme file.
/// Contains global palette tokens and one or more named theme definitions.
///
/// Example JSON:
/// {
///   "tokens": {
///     "violet": { "solid": "#7c6af7" },
///     "brand":  { "linear": ["#7c6af7", "#d060ff"] }
///   },
///   "themes": {
///     "dark": {
///       "AccentPurple": { "solid": "#7c6af7" },
///       "GradientBrand": "brand",
///       "BgBase": "#0d0d14"
///     }
///   }
/// }
/// </summary>
public sealed record ThemeFile
{
    /// <summary>
    /// Named palette tokens. Theme entries can reference these by name instead of
    /// repeating raw color values. Key is the token name, value is its brush definition.
    /// </summary>
    [JsonPropertyName("tokens")]
    public Dictionary<string, BrushValue> Tokens { get; init; } = [];

    /// <summary>
    /// Named theme definitions. Key is the theme ID (e.g. "dark", "rose-dark"),
    /// value maps convention names to brush values or token references.
    /// </summary>
    [JsonPropertyName("themes")]
    public Dictionary<string, Dictionary<string, ThemeValue>> Themes { get; init; } = [];
}

/// <summary>
/// Describes a brush. Exactly one of Solid, Linear, or Radial must be set.
/// </summary>
public sealed record BrushValue
{
    /// <summary>A solid color, e.g. "#7c6af7".</summary>
    [JsonPropertyName("solid")]
    public string? Solid { get; init; }

    /// <summary>
    /// Color stops for a horizontal linear gradient, distributed at equal offsets.
    /// e.g. ["#7c6af7", "#d060ff"]
    /// </summary>
    [JsonPropertyName("linear")]
    public string[]? Linear { get; init; }

    /// <summary>
    /// Color stops for a radial gradient, distributed at equal offsets.
    /// e.g. ["#7c6af7", "#d060ff"]
    /// </summary>
    [JsonPropertyName("radial")]
    public string[]? Radial { get; init; }

    /// <summary>Returns true when exactly one brush type is specified.</summary>
    public bool IsValid =>
        (Solid  is not null ? 1 : 0) +
        (Linear is not null ? 1 : 0) +
        (Radial is not null ? 1 : 0) == 1;
}

/// <summary>
/// A value inside a theme definition. In JSON this is either a string
/// (a token name or a hex color) or a BrushValue object.
/// Deserialized by ThemeValueConverter.
/// </summary>
public sealed record ThemeValue
{
    /// <summary>
    /// A token name (e.g. "violet") or a direct hex color (e.g. "#7c6af7").
    /// Used when the JSON value is a plain string.
    /// </summary>
    public string? Reference { get; init; }

    /// <summary>
    /// An inline brush definition. Used when the JSON value is an object.
    /// </summary>
    public BrushValue? Brush { get; init; }

    /// <summary>True when this value is a string reference rather than an inline brush.</summary>
    public bool IsReference => Reference is not null;
}
