# Inandu.Grid.Extensions

**One extension method turns an [inandu-grid](https://github.com/inandusolutions/inandu-grid)
`serverSide` request into a single paged SQL query.** No hand-rolled dynamic sort/filter builder,
no OData, no GraphQL layer to stand up — `ToInanduGrid()` / `ToInanduGridAsync()` read the grid's
own wire contract (multi-column sort, free-text search, per-column filters, grouping, aggregates,
keyset paging) and do the query work for you, over plain `IEnumerable<T>` or EF Core `IQueryable<T>`.

[![NuGet](https://img.shields.io/nuget/v/Inandu.Grid.Extensions.svg)](https://www.nuget.org/packages/Inandu.Grid.Extensions)
[![downloads](https://img.shields.io/nuget/dt/Inandu.Grid.Extensions.svg)](https://www.nuget.org/packages/Inandu.Grid.Extensions)
[![CI](https://github.com/inandusolutions/inandu-grid-extensions/actions/workflows/ci.yml/badge.svg)](https://github.com/inandusolutions/inandu-grid-extensions/actions/workflows/ci.yml)
[![license](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%2010.0-512bd4)
[![grid demo](https://img.shields.io/badge/grid%20demo-live-0e7c74)](https://inandusolutions.github.io/inandu-grid/)

## Try it in 30 seconds

```bash
dotnet add package Inandu.Grid.Extensions
```

```csharp
using Inandu.Grid.Extensions;

app.MapGet("/api/products", (HttpRequest request) =>
    _products.ToInanduGrid(request.QueryString.Value)); // { data, total, page, pageSize, pageCount }
```

That single call reads `page`/`sort`/`q`/column filters straight off the query string and returns
just the page the grid asked for — see [Quick start](#quick-start) below for the EF Core (async)
and ASP.NET Core binding variants.

## Who is this for?

- **.NET / ASP.NET Core APIs** backing an `<inandu-grid serverSide>` (or any client speaking the
  same [query-param contract](docs/request-contract.md)) that don't want to hand-write dynamic
  LINQ sort/filter/paging for every entity.
- **EF Core apps** that want the database to do the work — sort/filter/paging translate to SQL, so
  only one page is ever fetched, not the whole table scanned in memory.
- **Teams that want the query surface locked down** — per-column allow-lists, a query cost guard,
  and a `ThrowOnUnknownField` mode for public endpoints.

**When not to use it:** if you're not using inandu-grid's `serverSide` wire contract (or something
compatible with it) at all — this isn't a general-purpose OData or GraphQL server, see
[Not included](#not-included) below.

## Not included

Deliberately scoped to what the grid's wire contract needs, not a general-purpose query API:

- **No OData or GraphQL.** The query-string contract is inandu-grid's own (documented in
  [docs/request-contract.md](docs/request-contract.md)), not a spec-compliant OData/GraphQL server.
- **No built-in caching layer.** Every request runs a real query; add your own caching (response
  caching, a distributed cache) in front of it if you need one.
- **.NET only.** There's no equivalent for other backend stacks — the contract itself is plain
  query-string params, so a Node/Python/Go backend can implement it by hand against the same
  [request contract](docs/request-contract.md) docs, just not with this library.

### Packages

| Package | For |
|---|---|
| `Inandu.Grid.Extensions` | the core `ToInanduGrid()` — zero dependencies |
| `Inandu.Grid.Extensions.EntityFrameworkCore` | `ToInanduGridAsync()` — `CountAsync` + `ToListAsync` |
| `Inandu.Grid.Extensions.AspNetCore` | `[FromInanduGrid]` model binder + minimal-API binding |

Multi-targets **`net8.0`** and **`net10.0`**.

---

## Install

```bash
dotnet add package Inandu.Grid.Extensions
```

## Quick start

### ASP.NET Core (in-memory or any `IEnumerable<T>`)

```csharp
using Inandu.Grid.Extensions;

app.MapGet("/api/products", (HttpRequest request) =>
{
    InanduGridResult<Product> page = _products.ToInanduGrid(request.QueryString.Value, options =>
    {
        options.DefaultPageSize   = 25;
        options.MaxPageSize       = 200;
        options.SearchableFields  = new() { nameof(Product.Name), nameof(Product.Sku) };
    });

    return Results.Ok(page); // { data: [...], total: 1234, page: 1, pageSize: 25, pageCount: 50 }
});
```

### MVC controller

```csharp
[HttpGet]
public ActionResult<InanduGridResult<Product>> Get()
    => _products.ToInanduGrid(Request.QueryString.Value);
```

### EF Core (async)

```bash
dotnet add package Inandu.Grid.Extensions.EntityFrameworkCore
```

```csharp
using Inandu.Grid.Extensions.EntityFrameworkCore;

app.MapGet("/api/orders", (HttpRequest req, AppDb db, CancellationToken ct) =>
    db.Orders.AsNoTracking().ToInanduGridAsync(req.QueryString.Value, ct: ct));
```

`ToInanduGridAsync` runs `CountAsync` + `ToListAsync` under the hood. There are projected
(`ToInanduGridAsync(selector, …)`) and grouped (`ToInanduGridGroupedAsync`) overloads too. Without
the package, `ApplyInanduGridQuery` gives you the composed `FilteredQuery` / `PagedQuery` to
`await` yourself.

### `[FromInanduGrid]` — ASP.NET Core binding

```bash
dotnet add package Inandu.Grid.Extensions.AspNetCore
```

```csharp
using Inandu.Grid.Extensions.AspNetCore;

// MVC controller
[HttpGet]
public ActionResult<InanduGridResult<Product>> Get([FromInanduGrid] InanduGridRequest request)
    => _products.ToInanduGrid(InanduGridOptions.For(request));

// …or bind every InanduGridRequest parameter without the attribute
builder.Services.AddControllers().AddInanduGridModelBinding();

// minimal API
app.MapGet("/api/products", async (HttpRequest req) =>
    _products.ToInanduGrid(InanduGridOptions.For(await InanduGridBinding.FromHttpRequestAsync(req))));
```

`InanduGridBinding` reads the query string, or a JSON body for a POST.

### Bind the request yourself (JSON body, gRPC, tests…)

`InanduGridRequest` is a plain DTO. `@inandu-solutions/grid-pro` POSTs a body shaped
`{ sort: [{ field, direction }], page, pageSize, query, columnFilters }` — deserialize it straight
into `InanduGridRequest` (a bundled `System.Text.Json` converter accepts `"asc"` / `"desc"` and the
`"-field"` token form):

```csharp
app.MapPost("/api/products/query", (InanduGridRequest request) =>
    _products.ToInanduGrid(request, o => o.MaxPageSize = 200));
```

## Return a DTO, not the entity

`ToInanduGrid<TSource, TResult>(selector, …)` sorts and filters on the entity, then projects the
page — over EF Core the projection is part of the SQL, so only the DTO's columns are read:

```csharp
db.Orders.ToInanduGrid(
    o => new OrderDto(o.Id, o.Reference, o.Customer.Name, o.Total),
    Request.QueryString.Value,
    cfg => cfg.FieldMap = new() { ["customer"] = "Customer.Name" }); // filter/sort by Customer.Name
```

## Advanced filter (nested AND / OR)

`@inandu-solutions/grid-pro`'s query builder can serialise its tree with `advancedQueryToRestParams`
into a `filter=<json>` param. This library parses it (`InanduGridRequest.Filter` / `.AdvancedFilter`)
and applies it as an extra `AND` — operators include `between`, `notContains`, `isTrue/isFalse`,
`isEmpty/isNotEmpty`. See **[docs/advanced-filter.md](docs/advanced-filter.md)**.

## Server-side grouping

`ToInanduGridGrouped()` — `groupBy=region,category` returns the level's `{ key, count }` groups;
`groupKeys=EMEA` drills in (the next level, or the group's rows past the last `groupBy`). Filters,
free-text and the advanced filter all still apply. See **[docs/grouping.md](docs/grouping.md)**.

```csharp
var result = db.Sales.ToInanduGridGrouped(InanduGridOptions.FromQueryString(qs));
return result.IsLeaf ? Results.Ok(result.Rows) : Results.Ok(result.Groups);
```

## Aggregate totals

`aggregate=sum:amount,avg:rating,count:*,min:createdOn,max:price` → computed over the **filtered**
set (not the page) into `InanduGridResult<T>.Aggregations` (`{ "sum:amount": 91234.5, … }`) — for
the grid's totals row.

## Keyset (cursor) pagination

For deep pages, `after=<cursor>` seeks past the last row instead of `Skip`-ing over everything:

```csharp
var page = db.Orders.ToInanduGrid("?pageSize=50&sort=-createdOn,id", o => o.EnableKeyset = true);
// page.NextCursor -> next request adds &after=<NextCursor>;  o.IncludeTotal = false skips the COUNT
```

The sort columns are the cursor — include a unique tie-breaker (`,id`). A missing / stale cursor
falls back to offset paging. See **[docs/keyset-pagination.md](docs/keyset-pagination.md)**.

## Per-column configuration

```csharp
o.Column("customer").Path("Customer.Name").Searchable();
o.Column("price").Filterable(FilterOperator.GreaterThanOrEqual, FilterOperator.LessThanOrEqual);
o.Column("internalNote").NotFilterable().Sortable(false);
```

A disallowed operator / sort / filter is dropped — or, with `o.ThrowOnUnknownField = true`, throws
`InanduGridRequestException` (its `Errors` map is `ValidationProblemDetails`-shaped). See
**[docs/column-config.md](docs/column-config.md)**.

## Query cost guard

`o.Limits` caps how large a request can be (conditions, sort columns, advanced-filter depth /
nodes, `in`-list length, group levels, aggregations). `o.OnLimitExceeded` is `Trim` (default) or
`Reject` (throw). Defaults are generous; tighten for a public endpoint.

## Minimal-API one-liners (`Inandu.Grid.Extensions.AspNetCore`)

```csharp
app.MapInanduGrid("/api/products", products);
app.MapInanduGridGrouped("/api/products/groups", ctx => ctx.RequestServices.GetRequiredService<AppDb>().Products);
app.MapInanduGridDistinct("/api/products/distinct/{field}", products);
```

Using Swagger/OpenAPI? Minimal-API endpoints that read the query string by hand (which is what all
of the above do) are invisible to Swashbuckle by default — `page`, `sort`, `q` and friends show up
as undocumented. See [docs/openapi-swagger.md](docs/openapi-swagger.md) for the ~50-line filter the
[playground](playground/) uses to document them properly.

## The Angular side

Wire the grid's `serverSide` outputs to a request and bind the response:

```html
<inandu-grid serverSide
  [data]="rows()" [totalItems]="total()" [loading]="loading()"
  (sortChange)="onSort($event)" (pageChange)="onPage($event)" (filterChange)="onFilter($event)">
  …
</inandu-grid>
```

Either hand-build the query string, or let `@inandu-solutions/grid-pro`'s
`createInanduGridDataSource` do it — the params it emits are exactly what this package parses. See
the [playground](playground/) for a full working example, and the
[**live grid demo**](https://inandusolutions.github.io/inandu-grid/) for the front end this
serves.

## Request contract

See **[docs/request-contract.md](docs/request-contract.md)** for the full parameter list. In short:

| Param | Meaning |
|---|---|
| `page`, `pageSize` | 1-based page. |
| `offset`, `limit` | Row window (block model); wins over `page`/`pageSize`. |
| `sort` | Comma-separated; `-` prefix = descending. Repeatable. |
| `q` | Free-text search across `SearchableFields`. |
| `{field}_{op}` | A filter, `op` ∈ `eq neq contains startsWith endsWith gt gte lt lte in`. |
| `filter` | JSON advanced-filter tree (nested AND / OR). |
| `groupBy`, `groupKeys` | Server-side grouping + drill-down (`ToInanduGridGrouped`). |
| `aggregate` | `sum:f,avg:f,count:*,min:f,max:f` totals over the filtered set. |
| `after` | Keyset cursor — seek past this row instead of `Skip`. |

## Configuration

See **[docs/options.md](docs/options.md)**. Highlights: `DefaultPageSize`, `MaxPageSize`,
`SearchableFields`, `StringComparison`, `FieldMap` (grid field → CLR path), `ThrowOnUnknownField`,
`Culture`.

## Build, test & benchmark

```bash
dotnet build
dotnet test                                                   # 165 tests, net8.0 + net10.0
dotnet run -c Release --project benchmarks/Inandu.Grid.Extensions.Benchmarks
```

The libraries multi-target `net8.0;net10.0` **when built with the .NET 10 SDK**; with only the
.NET 8 SDK they build `net8.0` alone (see the condition in each `.csproj`). Produce release
packages with the .NET 10 SDK so the `.nupkg`s ship both. See
[docs/publishing.md](docs/publishing.md).

### Benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) micro-benchmarks over 100,000 in-memory rows, one
benchmark per query shape (plain page, filter, sort, filter+sort+page, free-text, advanced filter,
grouping, projection). Reference numbers (i5-7200U, .NET 8, `short` job): **filter+sort+page ≈
10 ms, advanced-filter ≈ 4 ms, top-level grouping ≈ 12 ms** — treat these as ballpark, not a
guarantee, and run it on your own hardware for a number that matters to your deployment. Full
methodology and the complete benchmark list: [benchmarks/README.md](benchmarks/README.md). A
`workflow_dispatch`-triggered [Benchmarks workflow](.github/workflows/benchmarks.yml) runs the
same suite on GitHub's runners and uploads the results as a build artifact, for a reproducible
(if not hardware-comparable) run anyone can trigger.

## Contributing

Issues and PRs welcome on
[GitHub](https://github.com/inandusolutions/inandu-grid-extensions). Add tests for behaviour
changes and keep the core library dependency-free — see [CONTRIBUTING.md](CONTRIBUTING.md).
Questions and usage help: see [`.github/SUPPORT.md`](.github/SUPPORT.md) — prefer
[Discussions](https://github.com/inandusolutions/inandu-grid-extensions/discussions) over an issue.
For security reports, see [`.github/SECURITY.md`](.github/SECURITY.md) — don't open a public issue.

## License

MIT © Inandu SAS — see [LICENSE](LICENSE).
