using System.Linq;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inandu.Grid.Extensions.EntityFrameworkCore.Tests;

/// <summary>
/// <c>groupKeysEncoding=repeat</c> down to SQL: a key with a comma, an empty key and a key with a
/// leading space must each match exactly that group on every provider.
/// </summary>
public class EfGroupKeysEncodingTests : IClassFixture<ShopFixture>
{
    private readonly ShopFixture _fx;

    public EfGroupKeysEncodingTests(ShopFixture fx) => _fx = fx;

    private async Task<InanduGridGroupedResult<Contact>> RunAsync(string queryString)
    {
        await using var db = _fx.NewContext();
        return await db.Contacts.AsNoTracking().ToInanduGridGroupedAsync(queryString + "&pageSize=50");
    }

    private static string[] Keys(InanduGridGroupedResult<Contact> r) =>
        r.Groups!.Select(g => (string)g.Key!).OrderBy(k => k).ToArray();

    [Fact]
    public async Task A_key_with_a_comma_drills_into_that_group()
    {
        var r = await RunAsync("?groupBy=name,region&groupKeys=Smith, John&groupKeysEncoding=repeat");

        Assert.Equal(new[] { "APAC", "EMEA" }, Keys(r));
    }

    [Fact]
    public async Task The_comma_form_still_splits_on_the_same_data()
    {
        // "Smith, John" -> ["Smith", "John"]: the group "Smith" with region "John" does not exist.
        var r = await RunAsync("?groupBy=name,region&groupKeys=Smith, John");

        Assert.True(r.IsLeaf);
        Assert.Empty(r.Rows!);
    }

    [Fact]
    public async Task An_empty_key_drills_into_the_empty_group_without_shifting_the_path()
    {
        var r = await RunAsync("?groupBy=name,region&groupKeys=&groupKeysEncoding=repeat");

        Assert.Equal(new[] { "EMEA" }, Keys(r));
    }

    [Fact]
    public async Task A_key_with_a_leading_space_matches_exactly()
    {
        var padded = await RunAsync("?groupBy=name,region&groupKeys= padded&groupKeysEncoding=repeat");
        Assert.Equal(new[] { "EMEA" }, Keys(padded));

        // without the space it is a different group, and there is none
        var plain = await RunAsync("?groupBy=name,region&groupKeys=padded&groupKeysEncoding=repeat");
        Assert.True(plain.Groups is null || plain.Groups.Count == 0);
    }

    [Fact]
    public async Task Two_keys_reach_the_leaf_rows()
    {
        var r = await RunAsync("?groupBy=name,region&groupKeys=Smith, John&groupKeys=APAC&groupKeysEncoding=repeat");

        Assert.True(r.IsLeaf);
        Assert.Equal(new[] { 2 }, r.Rows!.Select(c => c.Id).ToArray());
    }
}
