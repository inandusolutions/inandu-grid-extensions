# Keyset (cursor) pagination

Offset paging (`Skip(N).Take(M)`) gets slower as `N` grows — the database still walks the first
`N` rows. **Keyset** paging instead remembers the last row you saw and asks for "the next M rows
*after* this one", which stays fast at any depth.

## Turn it on

Keyset activates automatically whenever the request carries an `after` cursor. To emit a cursor
from the first page too (so a client can page purely by cursor), set `EnableKeyset`:

```csharp
var page = source.ToInanduGrid("?pageSize=50&sort=-createdOn,id", o => o.EnableKeyset = true);
```

The response gains:

| Field | |
|---|---|
| `nextCursor` | Pass as the next request's `after`. `null` on the last page. |
| `hasMore` | `true` when more rows follow. `null` for plain offset paging. |

Next request: `?pageSize=50&sort=-createdOn,id&after=<nextCursor>`.

`o.IncludeTotal = false` skips the `COUNT` entirely (`Total` comes back `-1`, `PageCount` `0`) —
the usual reason to choose keyset.

## Rules

- **`sort` is the cursor.** Every request in a keyset sequence must send the *same* `sort`.
- **Include a unique tie-breaker** as the last sort column (`,id`) — otherwise rows sharing the
  leading key can be skipped or repeated at a page boundary.
- Sort keys must be **orderable** (numbers, strings, dates, enums). A `bool`/`Guid` sort key or an
  unparseable cursor falls back to offset paging. When the **boundary row's key is null** a seek is
  impossible, so the `nextCursor` becomes an offset cursor (`{"s":N}`, still opaque to you) that resumes
  right after the rows already returned; following it always moves forward, and the sequence switches back to
  seeking as soon as a boundary key is non-null. This relies on the requests of one sequence being
  sequential with the same `sort` and filters.
- **Nulls sort first when ascending and last when descending, on every database.** In keyset mode a nullable sort
  key is ordered by "is it null" before its value, so the seek and `ORDER BY` agree whether the provider puts nulls
  first (SQL Server, SQLite, MySQL) or last (PostgreSQL, Oracle). Before 0.7.0 rows with a null key that sorted after the
  page boundary were never returned (descending on SQL Server / SQLite / MySQL, ascending on PostgreSQL). Plain offset
  paging keeps the database's own null order, and an index on the column can still serve it; with a nullable key, keyset
  mode adds the null test to `ORDER BY`, which an index may not cover.
- Multi-column sort builds a compound seek:
  `f1 <dir> v1  OR  (f1 = v1 AND f2 <dir> v2)  OR  …`.
- Over EF Core the seek is plain SQL; make sure the sort columns are indexed.

## Protecting the cursor

By default the cursor is **plain base64 JSON of the last row's sort values**. A client can decode it, so it can read
the value of every sort column of that row — even a column your DTO never returns — and it can build a cursor by hand
to jump to any position of the sequence. There is no SQL injection in that (the values are typed and parameterised) and
no way past your filters or allow-list (the seek is ANDed with them), but it is information you may not want to hand
out. There are two tools, and they combine.

### Keep a column out of cursors: `NoCursor()`

For a column the client may sort by but must not read back (a salary, a score, an internal rank):

```csharp
app.MapInanduGrid("/api/people", db.People.AsNoTracking(), o =>
{
    o.EnableKeyset = true;
    o.AllowUnconfiguredFields = false;
    o.Column("id").Sortable();
    o.Column("name").Sortable().Filterable();
    o.Column("salary").Sortable().NoCursor();     // sortable, but never travels in a cursor
});
```

A request whose `sort` includes a `NoCursor()` column is served by **offset paging**: no `nextCursor`, `hasMore` stays
`null`, an `after` that arrives is ignored, and `page` / `pageSize` work as usual. Requests sorted by other columns keep
using keyset. The cost is the one of any offset page (the database walks the skipped rows), so reserve it for columns
where that is acceptable.

### Encrypt and sign the cursor: `ProtectCursors(...)`

Turn it on and the cursor comes out **encrypted** (nothing to read), **authenticated** (any change is refused),
**bound to the `sort`** it was issued for, and optionally **expiring**. It is off by default, so existing clients and
cursors keep working until you switch it on.

**Anywhere, with a key you manage** (AES-256-GCM, no dependency):

```csharp
// once: generate 32 random bytes and keep them in your secret store (user secrets, Key Vault, an env variable…)
//   Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
byte[] key = Convert.FromBase64String(builder.Configuration["Grid:CursorKey"]!);

app.MapInanduGrid("/api/orders", db.Orders.AsNoTracking(), o =>
{
    o.EnableKeyset = true;
    o.ProtectCursors(key, lifetime: TimeSpan.FromHours(1));   // lifetime is optional; null = never expires
});
```

The key must be **the same on every server** that can answer the next page, and secret. Changing it invalidates the
cursors in flight (clients get the first page, or a `400` with `RejectInvalidCursors`).

**ASP.NET Core Data Protection** (`Inandu.Grid.Extensions.AspNetCore`), when the app already has a key ring:

```csharp
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo("/var/keys"));   // several servers: share the key ring

app.MapInanduGrid("/api/orders", db.Orders.AsNoTracking(), (o) =>
{
    o.EnableKeyset = true;
    o.ProtectCursors(app.Services.GetRequiredService<IDataProtectionProvider>(), TimeSpan.FromHours(1));
});
```

In a controller or any code that builds the options itself, it is the same call:

```csharp
var options = InanduGridOptions.FromQueryString(Request.QueryString.Value, o =>
{
    o.EnableKeyset = true;
    o.ProtectCursors(key);
});
var page = await db.Orders.ToInanduGridAsync(options);
```

What a protected sequence does and does not accept:

| `after` is… | Result |
|---|---|
| the `nextCursor` the server just issued, same `sort` | the next page |
| altered, made up, from another key / key ring | invalid |
| from before protection was turned on (plain base64) | invalid — a plain cursor is never accepted while a protector is set |
| issued for another `sort` (`-id` instead of `id`) | invalid |
| older than the `lifetime` | invalid |

An **invalid cursor** is ignored and the first page is served, which is how an unparseable cursor has always behaved. To
answer `400` instead (`after` named in the problem details), set `o.RejectInvalidCursors = true`. Prefer the default for
a public UI (an expired cursor just restarts the list) and `true` for an API you want to fail loudly.

Your own scheme (a different cipher, a signing service) is one interface:
`IInanduGridCursorProtector { string Protect(string cursor, string binding); string? Unprotect(string protectedCursor, string binding); }`
assigned to `o.CursorProtector`.

Two practical notes: a protected cursor is longer (about 4/3 of its plain size plus 37 bytes), so keep
`Limits.MaxCursorLength` above it (the default of 4096 is plenty); and `NoCursor()` and `ProtectCursors()` can be used
together, protecting the cursors of the columns that are allowed in one.

## Example loop

```csharp
string? cursor = null;
do
{
    var qs = $"?pageSize=200&sort=id{(cursor is null ? "" : $"&after={cursor}")}";
    var page = await db.Rows.AsNoTracking().ToInanduGridAsync(qs, o => { o.EnableKeyset = true; o.IncludeTotal = false; });
    Process(page.Data);
    cursor = page.NextCursor;
}
while (cursor is not null);
```
