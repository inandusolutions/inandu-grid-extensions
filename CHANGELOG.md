# Changelog

All notable changes to **Inandu.Grid.Extensions** are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Tests

- The EF Core tests now also run against **MySQL 8.4** (CI job `ef-mysql`, net8.0; locally they start a Testcontainers
  container: `INANDU_TEST_PROVIDER=mysql`). The keyset null-ordering fix of 0.7.0 holds on MySQL.

### Added

- **Protected keyset cursors (opt-in).** `o.ProtectCursors(key, lifetime)` encrypts and authenticates the cursor with AES-256-GCM
  (a client can no longer read the sort values in it or build one by hand), binds it to the `sort` it was issued for and, with a
  `lifetime`, expires it. `Inandu.Grid.Extensions.AspNetCore` adds `o.ProtectCursors(IDataProtectionProvider, lifetime)` over
  ASP.NET Core Data Protection, and `IInanduGridCursorProtector` / `o.CursorProtector` accept your own scheme. An unreadable
  cursor (altered, expired, for another sort, or plain while protection is on) is ignored like an unparseable one; set
  `o.RejectInvalidCursors = true` to answer `400` instead. Off by default: nothing changes until it is turned on.
- **`Column(...).NoCursor()`**: keeps a column out of keyset cursors. A request sorted by it is served by offset paging (no
  `nextCursor`), so its values never travel in a cursor. See
  [Protecting the cursor](docs/keyset-pagination.md#protecting-the-cursor).

## [0.7.0] - 2026-11-04

### Added

- Six input-size limits, checked before the input costs anything: `Limits.MaxQueryTextLength` (free-text `q`,
  default 200), `Limits.MaxFilterLength` (advanced `filter` JSON, default 20000), `Limits.MaxQueryStringLength`
  (raw string given to `FromQueryString`, default 32768), `Limits.MaxCursorLength` (keyset `after`, default 4096; a
  longer one is treated as an invalid cursor), `Limits.MaxValueLength` (one filter value, default 1000) and
  `Limits.MaxFieldNameLength` (a field name in a sort, filter or group-by, default 128). They follow `OnLimitExceeded`
  (trim, or reject with a 400). An advanced filter above 256 KB is no longer parsed at all.
- The JSON body of a grid request is read at most 128 KB (`InanduGridBinding.DefaultMaxBodyBytes`) by the
  `MapInanduGrid*` helpers and `[FromInanduGrid]`; a larger body gets a 413. A new overload,
  `InanduGridBinding.FromHttpRequestAsync(request, maxBodyBytes, ct)`, sets another cap (`0` keeps the host's limit).
- The `MapInanduGrid*` endpoints log a warning once, when they are mapped, if they keep the permissive default
  (`AllowUnconfiguredFields = true`), pointing to deny-by-default. Behaviour is unchanged.

- Set filters with values that contain a comma: `inEncoding=repeat` takes each `{field}_in` occurrence as one value,
  exactly as sent (commas, empty values and padding survive), like `groupKeysEncoding=repeat` does for `groupKeys`.
  `{field}_none=1` is the explicitly empty set and matches no rows; an `in` list built by hand with no items now
  also matches no rows (it used to be ignored). See [docs/request-contract.md](docs/request-contract.md).

### Fixed

- Keyset paging no longer loses rows whose sort key is null. A null key was never returned when it sorted after the
  page boundary (descending on SQL Server, SQLite and MySQL; ascending on PostgreSQL, also for text), so a walk over
  a nullable `int?` descending visited 48 of 60 rows. Keyset mode now orders nulls first when ascending and last when
  descending on every provider, and the seek follows that order. **Behaviour change:** in keyset mode a nullable key
  adds a null test to `ORDER BY`, and on PostgreSQL ascending nulls now come first (they came last). See
  [docs/keyset-pagination.md](docs/keyset-pagination.md).
- A date without a time on the upper side of a range no longer drops the last day. `createdOn_lte=2026-06-30`,
  the column filter's `to` and an advanced filter's `lte` were compared against midnight, so every row of that day was
  missing on a `DateTime` / `DateTimeOffset` property. A bare `yyyy-MM-dd` now names the whole day (`lte` reads
  `< next day`, `gt` reads `>= next day`). A value with a time is still an exact instant; `DateOnly` was already right.
- Keyset paging over text no longer skips rows that differ only by case. The seek folded case (the filter default,
  `OrdinalIgnoreCase`) while the sort did not, so `a` and `A` were equal to the seek but ordered apart. The seek now
  orders text like the sort does: the CLR comparer in memory, the column's collation in a database.
- `offset` / `limit` paging returned the wrong window when the offset was not a multiple of the limit
  (`offset=30&limit=25` returned rows 25-49). It now returns exactly rows `[offset, offset + limit)`.
- A keyset sequence sorted on a nullable column no longer loops forever at a null boundary key: the cursor
  continues by offset until a non-null key is reached. Cursors from 0.6.x are still accepted.
- Numeric filter operands no longer accept a thousands separator, so `1,5` is not read as `15` (it is now
  an unparseable operand, ignored like any other).
- A `+` in a query-string parameter *name* decodes to a space, as it already did in values.
- The publish workflow only runs from a `v*` tag or `main`, takes its manual `version` input through the
  environment and checks it is a SemVer version; the benchmarks workflow takes its `filter` input the same way.

## [0.6.1] - 2026-11-04

### Fixed

- Keyset paging now builds its seek and its `nextCursor` only from the sort fields the options allow. Before, a sort
  on a field that `AllowUnconfiguredFields = false` or `Sortable(false)` hides was ignored for ordering but still
  encoded into the cursor, so that field's value could appear in `nextCursor` and a crafted `after` cursor could filter
  on it. This only affects setups that use keyset paging (`EnableKeyset` or `after`) together with a restricted field
  list; offset paging was never affected. If you do, please upgrade.
- The advanced `filter` now honours the operators a column allows with `Filterable(...)`, like the flat
  `{field}_{op}` parameters already did. `between` needs both `gte` and `lte` allowed.

## [0.6.0] - 2026-11-04

### Added

- `groupKeys` as a repeated parameter: with `groupKeysEncoding=repeat`, each `groupKeys` occurrence is one drill-down key
  taken exactly as sent, so a group whose key contains a comma, is empty or has edge whitespace can be drilled into.
  Without the marker the comma form is unchanged (split, trim, drop empties). See [docs/grouping.md](docs/grouping.md).
- Diagnostics: every grid query (sync and `…Async`, rows / grouped / distinct) is reported through an
  `ActivitySource` and a `Meter` named `Inandu.Grid.Extensions` (`InanduGridDiagnostics.SourceName` /
  `MeterName`): one activity per query, an `inandu.grid.requests` counter and an
  `inandu.grid.request.duration` histogram, tagged with the operation and the outcome
  (`ok` / `rejected` / `error`). Field names, filter values and row data are never recorded. See
  [docs/diagnostics.md](docs/diagnostics.md).
- OpenAPI: `MapInanduGrid`, `MapInanduGridGrouped` and `MapInanduGridDistinct` now tag their endpoints with the new
  `InanduGridEndpoint` metadata, and `InanduGridOpenApi.Parameters(...)` returns the query parameters they accept, so a
  Swashbuckle filter or a `Microsoft.AspNetCore.OpenApi` `IOpenApiOperationTransformer` can document them in a few
  lines. A tested transformer for .NET 10 is in docs/openapi-swagger.md. Hand-wired endpoints add
  `.WithMetadata(new InanduGridEndpoint())`.

## [0.5.0] - 2026-11-03

### Added

- `InanduGridOptions.AllowUnconfiguredFields` (default `true`). Set it to `false` for a
  deny-by-default endpoint: only fields declared with `o.Column(...)` can be filtered, sorted,
  grouped, listed as distinct values or aggregated, and free-text search covers only
  `SearchableFields` plus `.Searchable()` columns. Recommended for public endpoints and for the
  `ToInanduGrid(selector, …)` overloads, which query the source entity rather than the DTO.

### Changed

- `Inandu.Grid.Extensions.EntityFrameworkCore` (`net8.0`) now requires EF Core **8.0.11** or later
  instead of 9.0.x, so .NET 8 apps on EF Core 8 can use it without upgrading. Since 0.3.x the
  minimum had been raised to EF Core 9 by an automated dependency bump. The `net10.0` build still
  requires EF Core 10. Tests keep running against the latest EF Core.
- NuGet tags and the core package description now mention the React and Vue grids as well as Angular. Package API validation (`EnablePackageValidation`, baseline 0.4.1) now runs on every pack, so an accidental breaking change to the public API fails the build.

## [0.4.1] - 2026-11-03

### Security

- `ToInanduGridDistinct` / `MapInanduGridDistinct` and `groupBy` now honour the column allow-list.
  Previously a `.NotFilterable()` column's values could still be listed through the distinct-values
  endpoint or by grouping on it. Grouping and distinct now follow `CanFilter`: a disallowed field is
  dropped (no values / a leaf result) or, with `ThrowOnUnknownField`, rejected with
  `InanduGridRequestException`. Applies to the in-memory and EF Core (`…Async`) entry points.
- `aggregate=` (`min` / `max` / `sum` / `avg` / `count` on a field) honours `CanFilter` too, so the
  aggregate row can't reveal a `.NotFilterable()` column's values. `count:*` is unaffected.

### Fixed

- An unknown `groupBy` or distinct field no longer throws `ArgumentException` (an HTTP 500 from the
  minimal-API helpers): it is dropped, or rejected with `InanduGridRequestException` in strict mode.
- `MapInanduGrid`, `MapInanduGridGrouped` and `MapInanduGridDistinct` now return `400` with a
  `ValidationProblemDetails` body when the request is rejected (`ThrowOnUnknownField`, or a
  `Limits` cap with `OnLimitExceeded = Reject`), instead of letting `InanduGridRequestException`
  escape as an HTTP 500. The endpoints also declare `ProducesValidationProblem()` for OpenAPI.
- `MapInanduGridDistinct` without a field now answers with the same `ValidationProblemDetails` shape
  (`errors.field`) instead of an ad-hoc `{ "error": "…" }` body.

## [0.4.0] - 2026-10-14

### Added

- Swagger/OpenAPI: `MapInanduGrid` and `ToInanduGrid()` minimal-API endpoints now document their query
  parameters (`page`, `sort`, `q`, `filters`, `aggregate`, …) via a Swashbuckle operation filter.

### Changed

- The `net10.0` build of the EF Core package now targets EF Core 10 (`Microsoft.EntityFrameworkCore` 10.0.x);
  the `net8.0` build keeps EF Core 9.0.x, so existing .NET 8 consumers are unaffected. No public API change.
- Test projects moved from the deprecated `xunit` 2.9.3 to `xunit.v3`.

## [0.3.0] - 2026-09-08

### Added

- **Aggregate totals** — `aggregate=sum:amount,avg:rating,count:*,min:d,max:p` computed over the
  filtered set, in `InanduGridResult<T>.Aggregations` (keyed `"sum:amount"`).
- **Keyset (cursor) pagination** — `after=<cursor>` seeks past the last row instead of `Skip`;
  `InanduGridResult<T>.NextCursor` / `HasMore`. `InanduGridOptions.EnableKeyset` emits a cursor on
  every sorted result; `IncludeTotal = false` skips the `Count`. Enums are now orderable, so they
  work as sort keys.
- **Distinct values** — `ToInanduGridDistinct(field)` → `{ value, count }[]` for a set-filter
  checklist, honouring the request's other filters. `ToInanduGridDistinctAsync` in the EF package.
- **Per-column config** — `options.Column("price").Path(…).Filterable(…) / .NotFilterable() /
  .Sortable(false) / .Searchable(true) / .Label(…)`. A disallowed operator / sort / filter is
  skipped, or throws in strict mode.
- **Query cost guard** — `InanduGridLimits` (max conditions, sort columns, advanced-filter
  nodes / depth, `in`-list length, group levels, aggregations) with
  `OnLimitExceeded = Trim` (default) / `Reject`. `InanduGridRequestException` carries a
  per-location `Errors` map (shaped for `ValidationProblemDetails`).
- **`MapInanduGrid` / `MapInanduGridGrouped` / `MapInanduGridDistinct`** minimal-API helpers in the
  ASP.NET Core package.

### Changed

- `ThrowOnUnknownField` (and the new guard's `Reject` mode) now throw `InanduGridRequestException`
  instead of `ArgumentException`.

### Packaging

- **First release on [nuget.org](https://www.nuget.org/packages/Inandu.Grid.Extensions).**
- Source moved to GitHub — `RepositoryUrl` now points at
  <https://github.com/inandusolutions/inandu-grid-extensions>; tagged `v0.3.0`.

## [0.2.0] - 2026-09-07

### Added

- **DTO projection** — `ToInanduGrid<TSource, TResult>(selector, …)` and
  `ApplyInanduGridQuery<TSource, TResult>(selector, …)`. Sorting and filtering resolve against the
  entity; the page is projected with the selector (part of the SQL over EF), so the DTO never has
  to carry the filterable columns.
- **Advanced filter bridge** — `AdvancedFilterJson.Parse` reads the nested AND / OR tree
  `@inandu-solutions/grid-pro`'s `advancedQueryToRestParams` emits as the `filter=<json>` param
  (operators incl. `between`, `notContains`, `isTrue/isFalse`, `isEmpty/isNotEmpty`).
  `InanduGridRequest.Filter` (raw) / `.AdvancedFilter` (parsed); applied as an extra `AND` term.
- **Server-side grouping** — `ToInanduGridGrouped` + `InanduGridGroup` /
  `InanduGridGroupedResult<T>`. `groupBy=region,category` returns `{ key, count }` groups for the
  level; `groupKeys=…` drills in — the next level, or (past the last `groupBy`) the group's rows.
  Filters, free-text and the advanced filter apply throughout.
- New package **`Inandu.Grid.Extensions.EntityFrameworkCore`** — `ToInanduGridAsync` (flat /
  projected / grouped) running as SQL via `CountAsync` + `ToListAsync`.
- New package **`Inandu.Grid.Extensions.AspNetCore`** — `[FromInanduGrid]` model binder,
  `AddInanduGridModelBinding()` for attribute-less binding of any `InanduGridRequest` parameter,
  and `InanduGridBinding` (query string or JSON body) for minimal APIs.
- A **BenchmarkDotNet** project (`benchmarks/`) covering every query shape over 100k rows.

### Changed

- `ExpressionBuilder` refactored around a shared `BuildScalarOperator` used by both the flat and
  advanced-filter paths.

## [0.1.0] - 2026-09-07

### Packaging

- Multi-targets `net8.0` and `net10.0` (the `net10.0` target is added only when built with a
  .NET 10+ SDK — see `docs/publishing.md`).
- No runtime dependencies. XML docs and `README.md` are packed; symbols ship as `.snupkg`.

### Added

- `ToInanduGrid<T>(this IEnumerable<T>, InanduGridOptions?)` and the `IQueryable<T>`
  overload — apply an inandu-grid `serverSide` request (sort + free-text + per-column
  filters + paging) to a sequence and get back only the current page plus the total count.
- `ToInanduGrid<T>(this IEnumerable<T>/IQueryable<T>, InanduGridRequest, Action<InanduGridOptions>?)`
  convenience overloads.
- `ApplyInanduGridQuery<T>(this IQueryable<T>, InanduGridOptions?)` — returns the composed
  `FilteredQuery` / `PagedQuery` without materializing, so EF Core callers can use their own
  `CountAsync` / `ToListAsync`.
- `InanduGridRequest.Parse(...)` — binds the [request contract](docs/request-contract.md)
  (`page`, `pageSize`, `offset`, `limit`, `sort=-field`, `field_op=value`, `q`) from a query
  string or a key/value collection, matching `@inandu-solutions/grid-pro`'s
  `createInanduGridDataSource` / `createInanduGridServerRowModel`.
- `InanduGridColumnFilter` mirrors the core `InanduGridColumnFilterValue`
  (`text` / `min` / `max` / `from` / `to` / `bool` / `values`).
- Dynamic, dependency-free sorting and filtering over `System.Linq.Expressions`
  (no `System.Linq.Dynamic.Core`), with value coercion for the common primitive types,
  `DateTime`/`DateTimeOffset`/`Guid`/`enum`/`Nullable<T>`.
- `InanduGridResult<T>` (`Data`, `Total`, `Page`, `PageSize`, `PageCount`).
- ASP.NET Core minimal-API playground with a real `<inandu-grid serverSide>` front end.

[Unreleased]: https://github.com/inandusolutions/inandu-grid-extensions/compare/v0.7.0...HEAD
[0.7.0]: https://github.com/inandusolutions/inandu-grid-extensions/compare/v0.6.1...v0.7.0
[0.6.1]: https://github.com/inandusolutions/inandu-grid-extensions/compare/v0.6.0...v0.6.1
[0.6.0]: https://github.com/inandusolutions/inandu-grid-extensions/compare/v0.5.0...v0.6.0
[0.5.0]: https://github.com/inandusolutions/inandu-grid-extensions/compare/v0.4.1...v0.5.0
[0.4.1]: https://github.com/inandusolutions/inandu-grid-extensions/compare/v0.4.0...v0.4.1
[0.4.0]: https://github.com/inandusolutions/inandu-grid-extensions/releases/tag/v0.4.0
[0.3.0]: https://github.com/inandusolutions/inandu-grid-extensions/releases/tag/v0.3.0
[0.2.0]: https://github.com/inandusolutions/inandu-grid-extensions/blob/main/CHANGELOG.md#020---2026-09-07
[0.1.0]: https://github.com/inandusolutions/inandu-grid-extensions/blob/main/CHANGELOG.md#010---2026-09-07
