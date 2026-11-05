using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>
/// The keyset cursor carries the sort values of the last row. With a protector set it comes out encrypted and authenticated:
/// a client can neither read those values nor build or change a cursor, and it only works for the sort it was issued for.
/// </summary>
public class CursorProtectionTests
{
    private static readonly byte[] Key = RandomNumberGenerator.GetBytes(32);

    private static void Protected(InanduGridOptions o)
    {
        o.EnableKeyset = true;
        o.ProtectCursors(Key);
    }

    private static string Decoded(string cursor)
    {
        var padded = cursor.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => Now;
    }

    // -- the protector ----------------------------------------------------

    [Fact]
    public void A_cursor_round_trips_through_the_protector()
    {
        var protector = InanduGridCursorProtector.FromKey(Key);

        var sealedCursor = protector.Protect("[17]", "id:a");

        Assert.Equal("[17]", protector.Unprotect(sealedCursor, "id:a"));
    }

    [Fact]
    public void A_protected_cursor_does_not_show_what_it_carries()
    {
        var sealedCursor = InanduGridCursorProtector.FromKey(Key).Protect("[\"secret-salary-93000\"]", "salary:a");

        Assert.DoesNotContain("secret", sealedCursor);
        Assert.DoesNotContain("93000", sealedCursor);
        var raw = Encoding.UTF8.GetString(Convert.FromBase64String(sealedCursor.Replace('-', '+').Replace('_', '/') + new string('=', (4 - sealedCursor.Length % 4) % 4)));
        Assert.DoesNotContain("secret", raw);
    }

    [Fact]
    public void Two_cursors_for_the_same_row_differ()
    {
        var protector = InanduGridCursorProtector.FromKey(Key);

        Assert.NotEqual(protector.Protect("[17]", "id:a"), protector.Protect("[17]", "id:a"));
    }

    [Fact]
    public void An_altered_cursor_is_refused()
    {
        var protector = InanduGridCursorProtector.FromKey(Key);
        var sealedCursor = protector.Protect("[17]", "id:a");
        var flipped = sealedCursor[..^3] + (sealedCursor[^3] == 'A' ? 'B' : 'A') + sealedCursor[^2..];

        Assert.Null(protector.Unprotect(flipped, "id:a"));
        Assert.Null(protector.Unprotect(sealedCursor[..^4], "id:a"));
        Assert.Null(protector.Unprotect("not a cursor", "id:a"));
        Assert.Null(protector.Unprotect(string.Empty, "id:a"));
    }

    [Fact]
    public void A_cursor_made_with_another_key_is_refused()
    {
        var sealedCursor = InanduGridCursorProtector.FromKey(RandomNumberGenerator.GetBytes(32)).Protect("[17]", "id:a");

        Assert.Null(InanduGridCursorProtector.FromKey(Key).Unprotect(sealedCursor, "id:a"));
    }

    [Fact]
    public void A_cursor_only_works_for_the_sort_it_was_issued_for()
    {
        var protector = InanduGridCursorProtector.FromKey(Key);
        var sealedCursor = protector.Protect("[17]", "id:a");

        Assert.Null(protector.Unprotect(sealedCursor, "id:d"));
        Assert.Null(protector.Unprotect(sealedCursor, "name:a"));
    }

    [Fact]
    public void A_cursor_expires()
    {
        var clock = new Clock();
        var protector = InanduGridCursorProtector.FromKey(Key, TimeSpan.FromMinutes(10), clock);
        var sealedCursor = protector.Protect("[17]", "id:a");

        clock.Now = clock.Now.AddMinutes(9);
        Assert.Equal("[17]", protector.Unprotect(sealedCursor, "id:a"));

        clock.Now = clock.Now.AddMinutes(2);
        Assert.Null(protector.Unprotect(sealedCursor, "id:a"));
    }

    [Fact]
    public void Without_a_lifetime_a_cursor_never_expires()
    {
        var clock = new Clock();
        var protector = InanduGridCursorProtector.FromKey(Key, null, clock);
        var sealedCursor = protector.Protect("[17]", "id:a");

        clock.Now = clock.Now.AddYears(5);

        Assert.Equal("[17]", protector.Unprotect(sealedCursor, "id:a"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    public void The_key_must_be_32_bytes(int length)
    {
        Assert.Throws<ArgumentException>(() => InanduGridCursorProtector.FromKey(new byte[length]));
    }

    // -- in a paged sequence ----------------------------------------------

    [Fact]
    public void A_protected_sequence_visits_every_row_once()
    {
        var seen = new List<int>();
        string? cursor = null;

        for (var guard = 0; guard < 50; guard++)
        {
            var qs = "?pageSize=7&sort=name,id" + (cursor is null ? "" : $"&after={cursor}");
            var page = Sample.Many(40).ToInanduGrid(qs, Protected);
            seen.AddRange(page.Data.Select(p => p.Id));
            if (page.HasMore != true)
            {
                break;
            }

            cursor = page.NextCursor;
        }

        Assert.Equal(Enumerable.Range(1, 40), seen.OrderBy(i => i));
        Assert.Equal(40, seen.Distinct().Count());
    }

    [Fact]
    public void The_issued_cursor_is_not_readable_json()
    {
        var page = Sample.Many(40).ToInanduGrid("?pageSize=7&sort=name,id", Protected);

        Assert.NotNull(page.NextCursor);
        Assert.ThrowsAny<Exception>(() => System.Text.Json.JsonDocument.Parse(Decoded(page.NextCursor!)));
        Assert.DoesNotContain("Product", page.NextCursor);
    }

    [Fact]
    public void Without_a_protector_the_cursor_is_unchanged()
    {
        var page = Sample.Many(40).ToInanduGrid("?pageSize=7&sort=id", o => o.EnableKeyset = true);

        Assert.Equal("{\"k\":[7],\"s\":7}", Decoded(page.NextCursor!));
    }

    [Fact]
    public void A_forged_cursor_is_ignored_and_the_first_page_is_served()
    {
        var forged = Convert.ToBase64String(Encoding.UTF8.GetBytes("[30]")).TrimEnd('=');

        var page = Sample.Many(40).ToInanduGrid($"?pageSize=7&sort=id&after={forged}", Protected);

        Assert.Equal(Enumerable.Range(1, 7), page.Data.Select(p => p.Id)); // not rows 31..37
    }

    [Fact]
    public void A_cursor_from_before_protection_was_turned_on_is_not_accepted()
    {
        var plain = Sample.Many(40).ToInanduGrid("?pageSize=7&sort=id", o => o.EnableKeyset = true).NextCursor;

        var page = Sample.Many(40).ToInanduGrid($"?pageSize=7&sort=id&after={plain}", Protected);

        Assert.Equal(Enumerable.Range(1, 7), page.Data.Select(p => p.Id));
    }

    [Fact]
    public void A_tampered_cursor_is_rejected_when_asked_to()
    {
        var ex = Assert.Throws<InanduGridRequestException>(() =>
            Sample.Many(40).ToInanduGrid("?pageSize=7&sort=id&after=abcd", o =>
            {
                Protected(o);
                o.RejectInvalidCursors = true;
            }));

        Assert.Contains("after", ex.Errors.Keys);
    }

    [Fact]
    public void A_cursor_taken_to_another_sort_is_refused()
    {
        var byId = Sample.Many(40).ToInanduGrid("?pageSize=7&sort=id", Protected).NextCursor;

        var page = Sample.Many(40).ToInanduGrid($"?pageSize=7&sort=-id&after={byId}", Protected);

        Assert.Equal(Enumerable.Range(34, 7).Reverse(), page.Data.Select(p => p.Id)); // the first page of -id, not a continuation
    }

    [Fact]
    public void An_expired_cursor_is_ignored()
    {
        var clock = new Clock();
        void Configure(InanduGridOptions o)
        {
            o.EnableKeyset = true;
            o.CursorProtector = InanduGridCursorProtector.FromKey(Key, TimeSpan.FromMinutes(5), clock);
        }

        var first = Sample.Many(40).ToInanduGrid("?pageSize=7&sort=id", Configure);
        clock.Now = clock.Now.AddMinutes(6);
        var second = Sample.Many(40).ToInanduGrid($"?pageSize=7&sort=id&after={first.NextCursor}", Configure);

        Assert.Equal(Enumerable.Range(1, 7), second.Data.Select(p => p.Id));
    }

    [Fact]
    public void Protection_also_covers_the_offset_resume_cursor()
    {
        // No stock at all: a null boundary key turns the cursor into an offset one ({"s":N}); it is protected as well.
        var rows = Enumerable.Range(1, 12).Select(i => new Product { Id = i, Name = $"P{i}", Stock = null }).ToList();

        var page = rows.ToInanduGrid("?pageSize=5&sort=stock,id", Protected);

        Assert.NotNull(page.NextCursor);
        Assert.ThrowsAny<Exception>(() => System.Text.Json.JsonDocument.Parse(Decoded(page.NextCursor!)));
    }

    // -- columns that must not travel in a cursor ---------------------------

    [Fact]
    public void A_no_cursor_column_is_paged_by_offset_and_issues_no_cursor()
    {
        var page = Sample.Many(40).ToInanduGrid("?pageSize=7&sort=price,id", o =>
        {
            o.EnableKeyset = true;
            o.Column("price").NoCursor();
        });

        Assert.Equal(7, page.Data.Count);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void A_no_cursor_column_ignores_a_cursor_that_arrives()
    {
        var cursor = Sample.Many(40).ToInanduGrid("?pageSize=7&sort=id", o => o.EnableKeyset = true).NextCursor;

        var page = Sample.Many(40).ToInanduGrid($"?pageSize=7&sort=id&after={cursor}", o => o.Column("id").NoCursor());

        Assert.Equal(Enumerable.Range(1, 7), page.Data.Select(p => p.Id)); // offset page 1, not rows 8..14
    }

    [Fact]
    public void A_no_cursor_column_still_pages_by_page_number()
    {
        var page = Sample.Many(40).ToInanduGrid("?pageSize=7&page=3&sort=id", o => o.Column("id").NoCursor());

        Assert.Equal(Enumerable.Range(15, 7), page.Data.Select(p => p.Id));
    }

    [Fact]
    public void Other_columns_still_get_a_cursor()
    {
        var page = Sample.Many(40).ToInanduGrid("?pageSize=7&sort=id", o =>
        {
            o.EnableKeyset = true;
            o.Column("price").NoCursor();
        });

        Assert.NotNull(page.NextCursor);
    }
}
