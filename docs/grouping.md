# Server-side grouping

`ToInanduGridGrouped()` groups a data source server-side with **lazy drill-down** — the shape
`@inandu-solutions/grid-pro`'s `createInanduGridServerRowModel` grouped mode expects.

## Params

| Param | Meaning |
|---|---|
| `groupBy` | Group fields, outermost first — `groupBy=region,category`. May be dotted paths. |
| `groupKeys` | The already-expanded path for a drill-down — `groupKeys=EMEA` or `groupKeys=EMEA,2026`. Each value is matched with `eq` against the matching `groupBy` field. Keys that contain a comma, are empty or have edge whitespace need the repeated form below. |
| `groupKeysEncoding` | `repeat` switches `groupKeys` to the repeated form. |

Filters, free-text and the advanced filter all apply throughout. The first `sort` criterion orders
the groups: a field of `count` orders by group size, anything else by the group key; ascending
unless the criterion is descending.

## Response — `InanduGridGroupedResult<T>`

| Property | |
|---|---|
| `Groups` | `IReadOnlyList<InanduGridGroup>` (`Field`, `Key`, `Count`) — the level's groups. `null` at a leaf. |
| `Rows` | `IReadOnlyList<T>` — the rows of a fully drilled-down group. `null` above a leaf. |
| `Total` | group count (or, at a leaf, row count) after filters. |
| `Page` / `PageSize` | paging applied to `Groups` (or `Rows`). |
| `Level` | how many `groupBy` levels are already expanded (== `groupKeys` length). |
| `IsLeaf` | `true` when `Rows` is populated (no more grouping levels below). |

## Flow

```
GET /api/sales/groups?groupBy=region,product          -> { groups: [{key:"EMEA",count:812}, …], isLeaf:false, level:0 }
GET …?groupBy=region,product&groupKeys=EMEA           -> { groups: [{key:"Widget",count:145}, …], isLeaf:false, level:1 }
GET …?groupBy=region,product&groupKeys=EMEA,Widget    -> { rows:  [ …the 145 rows… ],             isLeaf:true,  level:2 }
```

### Keys with commas, empty keys, padded keys

The comma form splits on commas, trims each key and drops empty ones, so a group called `Smith, John`,
an empty group, or a group named `" padded "` cannot be drilled into that way (the path would be wrong).
Send `groupKeysEncoding=repeat` and repeat `groupKeys`, **one key per occurrence, taken exactly as is**:

```
GET ...?groupBy=name,region&groupKeys=Smith, John&groupKeysEncoding=repeat
GET ...?groupBy=name,region&groupKeys=Smith, John&groupKeys=APAC&groupKeysEncoding=repeat
GET ...?groupBy=name,region&groupKeys=&groupKeysEncoding=repeat                  # the group whose name is empty
```

URL-encode each value as usual (`groupKeys=Smith%2C%20John`). Without the marker nothing changes, so
existing clients keep working. A server older than the release that introduced the marker ignores it and
would split the key, so use the repeated form only against a server that supports it. `grid-pro` switches
to it by itself, and only for a path that needs it.

```csharp
var r = db.Sales.ToInanduGridGrouped(InanduGridOptions.FromQueryString(qs));
return r.IsLeaf ? Results.Ok(r.Rows) : Results.Ok(r.Groups);
```

Async, over EF Core (each level is one `GROUP BY` query):

```csharp
using Inandu.Grid.Extensions.EntityFrameworkCore;
var r = await db.Sales.AsNoTracking().ToInanduGridGroupedAsync(qs, ct: ct);
```

> Group keys are boxed to `object` after materialisation — a `System.Text.Json` `PropertyNamingPolicy`
> of camelCase serialises `InanduGridGroup` as `{ "field": …, "key": …, "count": … }`.
