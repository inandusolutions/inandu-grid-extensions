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
- Sort keys must be **orderable** (numbers, strings, dates, enums) and **non-null** on the boundary
  row. A null key, an unparseable cursor, or a `bool`/`Guid` sort key → the request transparently
  falls back to offset paging (you still get a `nextCursor` to continue with).
- Multi-column sort builds a compound seek:
  `f1 <dir> v1  OR  (f1 = v1 AND f2 <dir> v2)  OR  …`.
- Over EF Core the seek is plain SQL; make sure the sort columns are indexed.

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
