# Changelog

All notable changes to **Inandu.Grid.Extensions** are documented here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the
project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
[0.1.0]: https://inandu.visualstudio.com/DefaultCollection/grid-private/_git/grid-server-side-extensions
