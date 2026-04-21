using System.Text.Json;
using System.Text.Json.Serialization;

namespace ConstellaTTS.Core.Theme;

/// <summary>
/// Deserializes a ThemeValue from JSON.
/// A plain string becomes a Reference; an object becomes an inline BrushValue.
///
/// "AccentPurple": "#7c6af7"          → ThemeValue { Reference = "#7c6af7" }
/// "AccentPurple": "violet"           → ThemeValue { Reference = "violet"  }
/// "AccentPurple": { "solid": "..." } → ThemeValue { Brush = BrushValue { Solid = "..." } }
/// </summary>
public sealed class ThemeValueConverter : JsonConverter<ThemeValue>
{
    public override ThemeValue Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new ThemeValue { Reference = reader.GetString() };

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var brush = JsonSerializer.Deserialize<BrushValue>(ref reader, options);
            return new ThemeValue { Brush = brush };
        }

        throw new JsonException(
            $"Unexpected token '{reader.TokenType}' — expected a string or object.");
    }

    public override void Write(Utf8JsonWriter writer, ThemeValue value, JsonSerializerOptions options)
    {
        if (value.IsReference)
            writer.WriteStringValue(value.Reference);
        else
            JsonSerializer.Serialize(writer, value.Brush, options);
    }
}
