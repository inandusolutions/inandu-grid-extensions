using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Inandu.Grid.Extensions.Internal;

/// <summary>
/// Encodes / decodes the opaque <c>after</c> cursor — base64 of JSON: either an array of the last row's
/// sort-key values (the 0.6 form, still accepted) or an object <c>{"k":[…],"s":N}</c> carrying those
/// values plus <c>s</c>, how many rows precede the next page. When the boundary key is null a seek is
/// impossible, so the cursor degrades to <c>{"s":N}</c> and the next page is an offset page.
/// </summary>
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
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (!root.TryGetProperty("k", out root))
                {
                    return null;
                }
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var values = new List<object?>();
            foreach (var el in root.EnumerateArray())
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

    /// <summary>The number of rows that precede this cursor's page, when the cursor carries it.</summary>
    public static int? DecodeSkip(string? cursor)
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

            using var doc = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(padded)));
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("s", out var el)
                && el.ValueKind == JsonValueKind.Number
                && el.TryGetInt32(out var skip)
                && skip >= 0
                    ? skip
                    : null;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>An offset-only cursor, used when the boundary row's sort key is null and a seek is impossible.</summary>
    public static string EncodeSkip(int skip)
        => ToUrlSafe(JsonSerializer.Serialize(new Dictionary<string, int> { ["s"] = skip }));

    private static string ToUrlSafe(string json)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string Encode(IReadOnlyList<object?> keyValues, int? skipAfter = null)
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

        return ToUrlSafe(skipAfter is int s
            ? JsonSerializer.Serialize(new Dictionary<string, object?> { ["k"] = array, ["s"] = s })
            : JsonSerializer.Serialize(array));
    }
}
