# Changelog

All notable changes to **Inandu.Grid.Extensions** are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[Unreleased]: https://inandu.visualstudio.com/DefaultCollection/grid-private/_git/grid-server-side-extensions
[0.2.0]: https://inandu.visualstudio.com/DefaultCollection/grid-private/_git/grid-server-side-extensions
[0.1.0]: https://inandu.visualstudio.com/DefaultCollection/grid-private/_git/grid-server-side-extensions
