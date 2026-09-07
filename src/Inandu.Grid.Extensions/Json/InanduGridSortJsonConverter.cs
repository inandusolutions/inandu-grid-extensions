using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inandu.Grid.Extensions.Json;

/// <summary>
/// Reads an <see cref="InanduGridSort"/> from either the object shape the grid emits
/// (<c>{ "field": "name", "direction": "asc" }</c>) or a REST-style token string (<c>"-name"</c>).
/// Writes the object shape.
/// </summary>
public sealed class InanduGridSortJsonConverter : JsonConverter<InanduGridSort>
{
    /// <inheritdoc />
    public override InanduGridSort? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return InanduGridSort.ParseToken(reader.GetString());

            case JsonTokenType.StartObject:
                break;

            default:
                throw new JsonException($"Unexpected token {reader.TokenType} for {nameof(InanduGridSort)}.");
        }

        string? field = null;
        var direction = SortDirection.Ascending;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                continue;
            }

            var name = reader.GetString();
            reader.Read();

            if (string.Equals(name, "field", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "prop", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "property", StringComparison.OrdinalIgnoreCase))
            {
                field = reader.GetString();
            }
            else if (string.Equals(name, "direction", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(name, "dir", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(name, "sort", StringComparison.OrdinalIgnoreCase))
            {
                direction = ReadDirection(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            return null;
        }

        return new InanduGridSort(field!, direction);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, InanduGridSort value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("field", value.Field);
        writer.WriteString("direction", value.IsDescending ? "desc" : "asc");
        writer.WriteEndObject();
    }

    private static SortDirection ReadDirection(ref Utf8JsonReader reader)
    {
        if (reader.TokenType == JsonTokenType.Number)
        {
            return reader.GetInt32() == 1 ? SortDirection.Descending : SortDirection.Ascending;
        }

        var text = reader.GetString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return SortDirection.Ascending;
        }

        return text.Trim().ToLowerInvariant() switch
        {
            "desc" or "descending" or "-1" or "d" => SortDirection.Descending,
            _ => SortDirection.Ascending,
        };
    }
}
