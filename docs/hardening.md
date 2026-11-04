# Hardening a public endpoint

`ToInanduGrid()` and the `MapInanduGrid*` helpers turn a query string into a LINQ query. By default
that is **permissive**: any public property of the row type can be filtered, sorted, grouped, listed
and aggregated, which is right for an internal tool and wrong for a public API. This page is the
checklist for the second case.

> The library does not authenticate or authorise anything. Put the endpoint behind your normal
> `[Authorize]` / policy, and apply per-tenant filtering to the `IQueryable` **before** it reaches
> `ToInanduGrid()`, so a client can never widen its own scope.

## 1. Deny by default

```csharp
app.MapInanduGrid("/api/orders", db.Orders.AsNoTracking(), o =>
{
    o.AllowUnconfiguredFields = false;          // only declared columns are honoured
    o.Column("id").Sortable();
    o.Column("customer").Path("Customer.Name").Sortable().Filterable(FilterOperator.Equal, FilterOperator.Contains);
    o.Column("total").Sortable().Filterable();
    o.Column("internalNote").NotFilterable().Sortable(false);
});
```

- With `AllowUnconfiguredFields = false`, a property you did not declare cannot be filtered, sorted,
  grouped, listed or aggregated, and free-text search covers only `SearchableFields` plus columns
  marked `.Searchable()`.
- A `.NotFilterable()` column also cannot be **grouped, listed (distinct) or aggregated**, because those
  expose the same values as a filter. This was not true before 0.4.1 (GHSA-pp2g-g637-74p9): upgrade.
- This also applies to the `ToInanduGrid(selector, …)` projection overloads: filtering and sorting run
  on the **source entity**, not on the DTO you return. A DTO limits what is returned, not what is queryable.

## 2. Decide between silent and strict

| `ThrowOnUnknownField` | A forbidden field in the request |
| --- | --- |
| `false` (default) | is dropped without a word; the request still succeeds |
| `true` | `400` `ValidationProblemDetails` naming it (`sort`, `filter.<field>`, `groupBy`, `distinct.<field>`, `aggregate.<field>`) |

Silent is friendlier to stale clients; strict is easier to debug and to test. Pick one per endpoint.
`grid-pro` shows the server message in `error()` and the offending columns in `errorDetails()`.

## 3. Cap the cost of a request

| Option | Default | What it bounds |
| --- | --- | --- |
| `MaxPageSize` | 500 | rows per response |
| `Limits.MaxConditions` | 50 | flat filter conditions |
| `Limits.MaxSortColumns` | 5 | sort columns |
| `Limits.MaxAdvancedFilterNodes` / `MaxAdvancedFilterDepth` | 200 / 10 | advanced-filter tree size / nesting |
| `Limits.MaxInListItems` | 500 | items in one `in` list |
| `Limits.MaxGroupByLevels` | 5 | `groupBy` levels |
| `Limits.MaxAggregations` | 20 | `aggregate` functions |
| `Limits.MaxQueryTextLength` | 200 | length of the free-text `q` search |
| `Limits.MaxFilterLength` | 20000 | length of the advanced `filter` JSON |
| `Limits.MaxQueryStringLength` | 32768 | raw query string given to `FromQueryString`, checked before parsing |
| `Limits.MaxCursorLength` | 4096 | length of the keyset cursor (`after`), checked before it is decoded; over it, the cursor is invalid |
| `Limits.MaxValueLength` | 1000 | length of one filter value (a condition, an `in` item, a column filter's text / bounds / set values) |
| `Limits.MaxFieldNameLength` | 128 | length of a field name in a sort, filter or group-by |

A JSON body is read at most `InanduGridBinding.DefaultMaxBodyBytes` (128 KB) by the `MapInanduGrid*` helpers and
`[FromInanduGrid]`; a larger one gets a `413`. Call `InanduGridBinding.FromHttpRequestAsync(request, maxBodyBytes, ct)`
for another cap (`0` leaves the host's limit alone). The keyset cursor is Base64 of the last row's sort-key
values: readable by the client, so do not sort a keyset on a column whose value must stay private.

The `MapInanduGrid*` helpers also log a warning, once per mapped endpoint, when it keeps the permissive
default (`AllowUnconfiguredFields = true`): see section 1.

`OnLimitExceeded` is `Trim` (default: cut the request down) or `Reject` (throw, which the `MapInanduGrid*`
helpers turn into a `400`). On a public endpoint, lower these to what your UI actually needs.

## 4. Mind the database, not just the API

- Index the columns you allow in `Filterable()` and `Sortable()`. An allowed filter on an unindexed
  column is still a table scan, whatever the limits say.
- `Contains` and `EndsWith` cannot use a normal index. Prefer `StartsWith` / `Equal` on large tables:
  `o.Column("sku").Filterable(FilterOperator.Equal, FilterOperator.StartsWith)`.
- Grouping and aggregates run in the database for EF Core, over the whole filtered set. Allow them
  only on columns where that is cheap, or leave a column out of them with `.NotFilterable()`.
- Keyset paging (`EnableKeyset`) avoids deep `OFFSET` scans on big tables.
- `IncludeTotal = false` skips the `COUNT(*)` when the client does not need it.
- Use `AsNoTracking()` on read-only grids.

## 5. Things the library cannot do for you

- Authentication, authorisation and row-level security (see the note at the top).
- Rate limiting: use ASP.NET Core's rate limiter on the endpoint.
- Hiding values from the **response**: project to a DTO, and remember rule 1 for what stays queryable.
- Sensitive columns that should be searchable only by exact match: declare them
  `Filterable(FilterOperator.Equal)` rather than leaving the default operators.

## Reporting a vulnerability

See [`.github/SECURITY.md`](../.github/SECURITY.md). Do not open a public issue.
