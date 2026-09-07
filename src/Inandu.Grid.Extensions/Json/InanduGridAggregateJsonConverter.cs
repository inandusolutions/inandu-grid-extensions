using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Inandu.Grid.Extensions.Json;

/// <summary>
/// Reads an <see cref="InanduGridAggregate"/> from a token string (<c>"sum:amount"</c>) or an object
/// (<c>{ "function": "sum", "field": "amount" }</c>). Writes the token string.
/// </summary>
public sealed class InanduGridAggregateJsonConverter : JsonConverter<InanduGridAggregate>
{
    /// <inheritdoc />
    public override InanduGridAggregate? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return InanduGridAggregate.Parse(reader.GetString());

            case JsonTokenType.StartObject:
                break;

            default:
                throw new JsonException($"Unexpected token {reader.TokenType} for {nameof(InanduGridAggregate)}.");
        }

        string? function = null;
        string? field = null;

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

            if (string.Equals(name, "function", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "fn", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "op", StringComparison.OrdinalIgnoreCase))
            {
                function = reader.GetString();
            }
            else if (string.Equals(name, "field", StringComparison.OrdinalIgnoreCase))
            {
                field = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }

        return string.IsNullOrWhiteSpace(function) || string.IsNullOrWhiteSpace(field)
            ? null
            : InanduGridAggregate.Parse($"{function}:{field}");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, InanduGridAggregate value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Key);
}
