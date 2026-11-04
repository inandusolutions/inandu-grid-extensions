using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>
/// "createdOn_lte=2026-06-30" names a whole day. Read against that day's midnight it dropped every row from
/// the day itself, while the grid's local date-range filter includes the whole "to" day.
/// </summary>
public class DateEndOfDayTests
{
    private sealed class Event
    {
        public int Id { get; set; }
        public DateTime At { get; set; }
        public DateTimeOffset AtOffset { get; set; }
        public DateOnly Day { get; set; }
    }

    // A date with no zone is read in the server's local offset (DateTimeOffset.TryParse), so these rows use it too.
    // Ids by instant: 1 = Jun 29 23:59, 2 = Jun 30 00:00, 3 = Jun 30 14:00, 4 = Jun 30 23:59:59.9999999, 5 = Jul 1 00:00
    private static List<Event> Events => new[]
    {
        new DateTime(2026, 6, 29, 23, 59, 0),
        new DateTime(2026, 6, 30, 0, 0, 0),
        new DateTime(2026, 6, 30, 14, 0, 0),
        new DateTime(2026, 6, 30, 23, 59, 59).AddTicks(9_999_999 % 10_000_000),
        new DateTime(2026, 7, 1, 0, 0, 0),
    }
    .Select((at, i) => new Event { Id = i + 1, At = at, AtOffset = new DateTimeOffset(at, TimeZoneInfo.Local.GetUtcOffset(at)), Day = DateOnly.FromDateTime(at) })
    .ToList();

    private static int[] Ids(string query) => Events.ToInanduGrid(query, o => o.MaxPageSize = 100).Data.Select(e => e.Id).OrderBy(i => i).ToArray();

    [Fact]
    public void Lte_a_day_includes_the_whole_day()
        => Assert.Equal(new[] { 1, 2, 3, 4 }, Ids("?at_lte=2026-06-30"));

    [Fact]
    public void Gt_a_day_starts_at_the_next_day()
        => Assert.Equal(new[] { 5 }, Ids("?at_gt=2026-06-30"));

    [Fact]
    public void Gte_and_lt_a_day_keep_reading_from_the_start_of_that_day()
    {
        Assert.Equal(new[] { 2, 3, 4, 5 }, Ids("?at_gte=2026-06-30"));
        Assert.Equal(new[] { 1 }, Ids("?at_lt=2026-06-30"));
    }

    [Fact]
    public void A_day_range_covers_both_ends_in_full()
        => Assert.Equal(new[] { 2, 3, 4 }, Ids("?at_gte=2026-06-30&at_lte=2026-06-30"));

    [Fact]
    public void A_value_with_a_time_is_still_an_exact_instant()
    {
        Assert.Equal(new[] { 1, 2 }, Ids("?at_lte=2026-06-30T00:00:00"));
        Assert.Equal(new[] { 4, 5 }, Ids("?at_gt=2026-06-30T14:00:00"));
    }

    [Fact]
    public void DateTimeOffset_follows_the_same_rule()
    {
        Assert.Equal(new[] { 1, 2, 3, 4 }, Ids("?atOffset_lte=2026-06-30"));
        Assert.Equal(new[] { 5 }, Ids("?atOffset_gt=2026-06-30"));
    }

    [Fact]
    public void DateOnly_was_already_correct_and_is_unchanged()
    {
        Assert.Equal(new[] { 1, 2, 3, 4 }, Ids("?day_lte=2026-06-30"));
        Assert.Equal(new[] { 5 }, Ids("?day_gt=2026-06-30"));
    }

    [Fact]
    public void The_last_representable_day_does_not_overflow()
    {
        var rows = new List<Event> { new() { Id = 1, At = DateTime.MaxValue.Date } };

        var result = rows.ToInanduGrid("?at_lte=9999-12-31");

        Assert.Single(result.Data);
    }

    [Fact]
    public void A_column_filter_to_includes_the_whole_last_day()
    {
        var request = new InanduGridRequest { PageSize = 100 };
        request.ColumnFilters["at"] = new InanduGridColumnFilter { From = "2026-06-30", To = "2026-06-30" };

        var ids = Events.ToInanduGrid(InanduGridOptions.For(request)).Data.Select(e => e.Id).OrderBy(i => i).ToArray();

        Assert.Equal(new[] { 2, 3, 4 }, ids);
    }
}
