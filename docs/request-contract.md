# Request contract

`InanduGridRequest.Parse` reads the same parameters `@inandu-solutions/grid-pro`'s
`createInanduGridDataSource` and `createInanduGridServerRowModel` emit, so an endpoint that already
serves an `<inandu-grid serverSide>` needs no client changes.

## Paging

| Param | Type | Notes |
|---|---|---|
| `page` | int | **1-based.** Defaults to `1`. |
| `pageSize` | int | Defaults to `InanduGridOptions.DefaultPageSize` (25). Clamped to `MaxPageSize`. |
| `offset` | int | Row offset for the block/windowed model. |
| `limit` | int | Row count for the block/windowed model. |

When **both** `offset` and `limit` are present (and `limit > 0`) they win over `page` / `pageSize`;
the response's `page` is derived as `offset / limit + 1`. Aliases: `skip` = `offset`, `take` = `pageSize`.

## Sorting

| Param | Example | Meaning |
|---|---|---|
| `sort` | `sort=name` | ascending by `name` |
| | `sort=-createdOn` | descending by `createdOn` |
| | `sort=-priority,name` | descending `priority`, then ascending `name` |
| | `sort=priority&sort=-name` | same, as repeated params |

A leading `+` is allowed and ignored. Aliases: `orderby`, `order`.

## Free-text search

| Param | Meaning |
|---|---|
| `q` | Matched with `contains` against every field in `InanduGridOptions.SearchableFields` (OR-combined). `null` searchable-fields ⇒ every `string` property. Aliases: `query`, `search`, `term`. |

## Advanced filter

| Param | Meaning |
|---|---|
| `filter` | A JSON advanced-filter tree (nested AND / OR) — the value `@inandu-solutions/grid-pro`'s `advancedQueryToRestParams` emits. Parsed into `InanduGridRequest.AdvancedFilter`, applied as an extra `AND`. Alias: `advancedFilter`. Blank / malformed → ignored. See [advanced-filter.md](advanced-filter.md). |

## Grouping (`ToInanduGridGrouped`)

| Param | Meaning |
|---|---|
| `groupBy` | Comma-separated group fields, outermost first (`groupBy=region,category`). |
| `groupKeys` | The already-expanded path for a drill-down (`groupKeys=EMEA` / `groupKeys=EMEA,2026`); each value is `eq`-matched against the corresponding `groupBy` field. |

See [grouping.md](grouping.md).

## Aggregate totals

| Param | Meaning |
|---|---|
| `aggregate` | Comma-separated `function:field` — `sum` / `avg` / `min` / `max` / `count` (and `count:*` for all rows). Computed over the filtered set; lands in `InanduGridResult.Aggregations` keyed `"sum:amount"`. Alias: `aggregates`. |

## Keyset paging

| Param | Meaning |
|---|---|
| `after` | Opaque cursor (base64 of the last row's sort-key values). With a non-empty `sort`, the page is fetched by seeking past that row instead of `Skip`. Take it from a prior result's `nextCursor`. Alias: `cursor`. See [keyset-pagination.md](keyset-pagination.md). |

## Column filters

Flat form — one param per condition, key is `{field}_{operator}`:

| Operator token | Meaning | Example |
|---|---|---|
| `eq` | equals | `status_eq=Active` |
| `neq` (`ne`) | not equals | `status_neq=Archived` |
| `contains` | substring (string fields) | `name_contains=cable` |
| `startsWith` | prefix | `sku_startsWith=KB-` |
| `endsWith` | suffix | `name_endsWith=Pro` |
| `gt` | greater than | `price_gt=100` |
| `gte` (`ge`) | greater than or equal | `price_gte=20` |
| `lt` | less than | `stock_lt=10` |
| `lte` (`le`) | less than or equal | `createdOn_lte=2026-06-30` |
| `in` | one of a comma list | `status_in=Active,Draft` |

`field` may be a **dotted path** (`customer.name_contains=ann`), or a grid alias resolved through
`InanduGridOptions.FieldMap`. The operator is the segment after the **last** `_`; a key whose
suffix isn't a known operator is ignored (so `created_on=…` is not mistaken for a filter).

### `InanduGridColumnFilter` → operators

When you bind a JSON body, each column's control value (mirroring the core
`InanduGridColumnFilterValue`) expands the same way as grid-pro's `columnFilterToConditions`:

| Field set | Becomes |
|---|---|
| `text` | `contains` |
| `min` (finite number) | `gte` |
| `max` (finite number) | `lte` |
| `from` | `gte` |
| `to` | `lte` |
| `bool: "true"` / `"false"` | `eq true` / `eq false` |
| `values: [...]` (non-empty) | `in` — takes over the column; an empty array is *no constraint* |

## Value coercion

Operand strings are coerced to the target property's CLR type: `int` / `long` / `decimal` /
`double` / `bool` / `Guid` / `DateTime` / `DateTimeOffset` / `DateOnly` / `TimeOnly` / `TimeSpan` /
`enum` (by name, case-insensitive) and their `Nullable<T>`. Coercion failures make that one
condition a no-op rather than a 400/500. Numbers and dates parse with
`InanduGridOptions.Culture` (invariant by default).

Ordered comparison (`gt`/`gte`/`lt`/`lte`) is **not** emitted for `bool`, `Guid` or `enum` members —
use `eq` / `in` there.

## Response

`InanduGridResult<T>` serializes (camelCase, the ASP.NET Core default) as:

```json
{ "data": [ /* the page */ ], "total": 1234, "page": 1, "pageSize": 25, "pageCount": 50 }
```

Bind `data` to `[data]` and `total` to `[totalItems]`.
