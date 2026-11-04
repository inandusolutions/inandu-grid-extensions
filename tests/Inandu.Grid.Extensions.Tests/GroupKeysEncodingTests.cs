using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary><c>groupKeys</c> as a repeated parameter (<c>groupKeysEncoding=repeat</c>): keys that the comma form cannot carry.</summary>
public class GroupKeysEncodingTests
{
    private sealed record Person(int Id, string Name, string Region);

    private static readonly Person[] People =
    {
        new(1, "Smith, John", "EMEA"),
        new(2, "Smith, John", "APAC"),
        new(3, "Smith", "EMEA"),
        new(4, "", "EMEA"),
        new(5, " padded ", "EMEA"),
        new(6, "Plain", "EMEA"),
    };

    [Fact]
    public void The_comma_form_is_unchanged_it_splits_trims_and_drops_empties()
    {
        var request = InanduGridRequest.Parse("?groupKeys=Smith, John&groupKeys= EMEA ,,2026");

        Assert.Equal(new[] { "Smith", "John", "EMEA", "2026" }, request.GroupKeys);
    }

    [Fact]
    public void With_the_marker_each_occurrence_is_one_key_taken_as_is()
    {
        var request = InanduGridRequest.Parse("?groupKeys=Smith, John&groupKeys=&groupKeys= padded &groupKeysEncoding=repeat");

        Assert.Equal(new[] { "Smith, John", "", " padded " }, request.GroupKeys);
    }

    [Fact]
    public void The_marker_may_come_before_or_after_the_keys_and_is_not_a_filter()
    {
        var before = InanduGridRequest.Parse("?groupKeysEncoding=repeat&groupKeys=a,b");
        var after = InanduGridRequest.Parse("?groupKeys=a,b&groupKeysEncoding=repeat");

        Assert.Equal(new[] { "a,b" }, before.GroupKeys);
        Assert.Equal(new[] { "a,b" }, after.GroupKeys);
        Assert.Empty(before.Conditions);
        Assert.Empty(after.Conditions);
    }

    [Fact]
    public void Any_other_marker_value_keeps_the_comma_form()
    {
        var request = InanduGridRequest.Parse("?groupKeys=a,b&groupKeysEncoding=csv");

        Assert.Equal(new[] { "a", "b" }, request.GroupKeys);
    }

    [Fact]
    public void A_group_whose_key_has_a_comma_can_be_drilled_into()
    {
        var result = People.ToInanduGridGrouped(InanduGridOptions.FromQueryString(
            "?groupBy=name,region&groupKeys=Smith, John&groupKeysEncoding=repeat&pageSize=50"));

        // second level: the regions inside "Smith, John" - not the rows of "Smith" and "John"
        Assert.NotNull(result.Groups);
        Assert.Equal(new[] { "APAC", "EMEA" }, result.Groups!.Select(g => (string)g.Key!).OrderBy(k => k).ToArray());
    }

    [Fact]
    public void An_empty_key_and_a_padded_key_match_exactly_and_do_not_shift_the_path()
    {
        var empty = People.ToInanduGridGrouped(InanduGridOptions.FromQueryString(
            "?groupBy=name,region&groupKeys=&groupKeysEncoding=repeat&pageSize=50"));
        Assert.Equal(new[] { "EMEA" }, empty.Groups!.Select(g => (string)g.Key!).ToArray());

        var padded = People.ToInanduGridGrouped(InanduGridOptions.FromQueryString(
            "?groupBy=name,region&groupKeys= padded &groupKeysEncoding=repeat&pageSize=50"));
        Assert.Equal(new[] { "EMEA" }, padded.Groups!.Select(g => (string)g.Key!).ToArray());

        var twoLevels = People.ToInanduGridGrouped(InanduGridOptions.FromQueryString(
            "?groupBy=name,region&groupKeys=Smith, John&groupKeys=APAC&groupKeysEncoding=repeat&pageSize=50"));
        Assert.Equal(new[] { 2 }, twoLevels.Rows!.Select(p => p.Id).ToArray());
    }
}
