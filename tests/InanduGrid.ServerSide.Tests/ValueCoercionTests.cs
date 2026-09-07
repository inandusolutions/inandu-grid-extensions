using System;
using System.Globalization;
using InanduGrid.ServerSide.Internal;
using Xunit;

namespace InanduGrid.ServerSide.Tests;

public class ValueCoercionTests
{
    private static readonly IFormatProvider Inv = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData("42", typeof(int), 42)]
    [InlineData("42", typeof(long), 42L)]
    [InlineData("3.5", typeof(double), 3.5)]
    [InlineData("true", typeof(bool), true)]
    [InlineData("1", typeof(bool), true)]
    [InlineData("off", typeof(bool), false)]
    public void Coerces_primitives(string input, Type target, object expected)
    {
        Assert.True(ValueCoercion.TryCoerce(input, target, Inv, out var result));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Coerces_decimal_with_invariant_culture()
    {
        Assert.True(ValueCoercion.TryCoerce("1234.56", typeof(decimal), Inv, out var result));
        Assert.Equal(1234.56m, result);
    }

    [Fact]
    public void Coerces_datetime_and_guid_and_enum()
    {
        Assert.True(ValueCoercion.TryCoerce("2026-01-02T03:04:05Z", typeof(DateTime), Inv, out var dt));
        Assert.Equal(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), ((DateTime)dt!).ToUniversalTime());

        var guid = Guid.NewGuid();
        Assert.True(ValueCoercion.TryCoerce(guid.ToString(), typeof(Guid), Inv, out var g));
        Assert.Equal(guid, g);

        Assert.True(ValueCoercion.TryCoerce("archived", typeof(Status), Inv, out var s));
        Assert.Equal(Status.Archived, s);
    }

    [Fact]
    public void Unwraps_nullable_target()
    {
        Assert.True(ValueCoercion.TryCoerce("7", typeof(int?), Inv, out var result));
        Assert.Equal(7, result);

        Assert.True(ValueCoercion.TryCoerce(null, typeof(int?), Inv, out var n));
        Assert.Null(n);
    }

    [Fact]
    public void Already_correct_type_passes_through()
    {
        Assert.True(ValueCoercion.TryCoerce(5, typeof(int), Inv, out var result));
        Assert.Equal(5, result);
    }

    [Fact]
    public void Bad_input_returns_false_not_throws()
    {
        Assert.False(ValueCoercion.TryCoerce("not-a-number", typeof(int), Inv, out _));
        Assert.False(ValueCoercion.TryCoerce("not-a-date", typeof(DateTime), Inv, out _));
        Assert.False(ValueCoercion.TryCoerce("nope", typeof(Status), Inv, out _));
    }
}
