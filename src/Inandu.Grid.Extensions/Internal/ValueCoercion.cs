using System;
using System.Globalization;

namespace Inandu.Grid.Extensions.Internal;

/// <summary>Coerces a filter operand (usually a <see cref="string"/> off the query string) to the target property's CLR type.</summary>
internal static class ValueCoercion
{
    /// <summary>
    /// Tries to convert <paramref name="value"/> to <paramref name="targetType"/> (unwrapping
    /// <see cref="Nullable{T}"/>). Returns <c>false</c> — rather than throwing — when it can't, so a
    /// malformed filter becomes a no-op instead of a server error.
    /// </summary>
    public static bool TryCoerce(object? value, Type targetType, IFormatProvider formatProvider, out object? result)
    {
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        result = null;

        if (value is null)
        {
            return !type.IsValueType || Nullable.GetUnderlyingType(targetType) is not null;
        }

        if (type.IsInstanceOfType(value))
        {
            result = value;
            return true;
        }

        var text = value as string ?? Convert.ToString(value, formatProvider);
        if (text is null)
        {
            return false;
        }

        text = text.Trim();

        try
        {
            if (type == typeof(string))
            {
                result = text;
                return true;
            }

            if (type == typeof(bool))
            {
                if (bool.TryParse(text, out var b))
                {
                    result = b;
                    return true;
                }

                if (text is "1" or "yes" or "on") { result = true; return true; }
                if (text is "0" or "no" or "off") { result = false; return true; }
                return false;
            }

            if (type == typeof(Guid))
            {
                if (Guid.TryParse(text, out var g)) { result = g; return true; }
                return false;
            }

            if (type == typeof(DateTime))
            {
                if (DateTime.TryParse(text, formatProvider, DateTimeStyles.RoundtripKind, out var dt)) { result = dt; return true; }
                return false;
            }

            if (type == typeof(DateTimeOffset))
            {
                if (DateTimeOffset.TryParse(text, formatProvider, DateTimeStyles.RoundtripKind, out var dto)) { result = dto; return true; }
                return false;
            }

            if (type == typeof(DateOnly))
            {
                if (DateOnly.TryParse(text, formatProvider, DateTimeStyles.None, out var d)) { result = d; return true; }
                return false;
            }

            if (type == typeof(TimeOnly))
            {
                if (TimeOnly.TryParse(text, formatProvider, DateTimeStyles.None, out var t)) { result = t; return true; }
                return false;
            }

            if (type == typeof(TimeSpan))
            {
                if (TimeSpan.TryParse(text, formatProvider, out var ts)) { result = ts; return true; }
                return false;
            }

            if (type.IsEnum)
            {
                if (Enum.TryParse(type, text, ignoreCase: true, out var e)) { result = e; return true; }
                return false;
            }

            if (type == typeof(char))
            {
                if (text.Length == 1) { result = text[0]; return true; }
                return false;
            }

            if (IsNumeric(type))
            {
                if (decimal.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands, formatProvider, out var dec))
                {
                    result = Convert.ChangeType(dec, type, formatProvider);
                    return true;
                }

                return false;
            }

            // last resort
            result = Convert.ChangeType(text, type, formatProvider);
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            result = null;
            return false;
        }
    }

    private static bool IsNumeric(Type type)
        => type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)
           || type == typeof(sbyte) || type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort)
           || type == typeof(double) || type == typeof(float) || type == typeof(decimal);
}
