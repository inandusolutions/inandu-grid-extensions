using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Inandu.Grid.Extensions.Internal;

/// <summary>Encodes / decodes the opaque <c>after</c> cursor — base64 of a JSON array of the last row's sort-key values.</summary>
internal static class KeysetCursor
{
    public static List<object?>? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var values = new List<object?>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                values.Add(el.ValueKind switch
                {
                    JsonValueKind.String => el.GetString(),
                    JsonValueKind.Number => el.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    JsonValueKind.Null => null,
                    _ => el.GetRawText(),
                });
            }

            return values;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public static string Encode(IReadOnlyList<object?> keyValues)
    {
        var array = new object?[keyValues.Count];
        for (var i = 0; i < keyValues.Count; i++)
        {
            array[i] = keyValues[i] switch
            {
                null => null,
                DateTime dt => dt.ToString("O"),
                DateTimeOffset dto => dto.ToString("O"),
                DateOnly d => d.ToString("O"),
                TimeOnly t => t.ToString("O"),
                Guid g => g.ToString(),
                var v => v,
            };
        }

        var json = JsonSerializer.Serialize(array);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_'); // URL-safe
    }
}
