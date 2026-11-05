using System;
using System.Linq;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Inandu.Grid.Extensions.AspNetCore.Tests;

public class CursorDataProtectionTests
{
    private sealed class Row
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    private static InanduGridOptions Options(string query, IDataProtectionProvider provider, Action<InanduGridOptions>? more = null)
        => InanduGridOptions.FromQueryString(query, o =>
        {
            o.EnableKeyset = true;
            o.ProtectCursors(provider);
            more?.Invoke(o);
        });

    private static System.Collections.Generic.List<Row> Rows()
        => Enumerable.Range(1, 30).Select(i => new Row { Id = i, Name = $"Product {i:D2}" }).ToList();

    [Fact]
    public void A_sequence_walks_through_Data_Protection_and_the_cursor_is_opaque()
    {
        var provider = new EphemeralDataProtectionProvider();

        var first = Rows().ToInanduGrid(Options("?pageSize=10&sort=name,id", provider));
        var second = Rows().ToInanduGrid(Options($"?pageSize=10&sort=name,id&after={first.NextCursor}", provider));

        Assert.DoesNotContain("Product", first.NextCursor);
        Assert.Equal(Enumerable.Range(11, 10), second.Data.Select(p => p.Id));
    }

    [Fact]
    public void A_cursor_from_another_key_ring_is_ignored()
    {
        var first = Rows().ToInanduGrid(Options("?pageSize=10&sort=id", new EphemeralDataProtectionProvider()));

        var page = Rows().ToInanduGrid(Options($"?pageSize=10&sort=id&after={first.NextCursor}", new EphemeralDataProtectionProvider()));

        Assert.Equal(Enumerable.Range(1, 10), page.Data.Select(p => p.Id));
    }

    [Fact]
    public void A_cursor_for_another_sort_is_refused_and_can_be_rejected()
    {
        var provider = new EphemeralDataProtectionProvider();
        var byId = Rows().ToInanduGrid(Options("?pageSize=10&sort=id", provider)).NextCursor;

        Assert.Throws<InanduGridRequestException>(() =>
            Rows().ToInanduGrid(Options($"?pageSize=10&sort=-id&after={byId}", provider, o => o.RejectInvalidCursors = true)));
    }

    [Fact]
    public void A_lifetime_must_be_positive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DataProtectionCursorProtector(new EphemeralDataProtectionProvider(), TimeSpan.Zero));
    }
}
