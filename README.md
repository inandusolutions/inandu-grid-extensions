# Inandu.Grid.Extensions

Server-side **paging, sorting and filtering** for
[`@inandu-solutions/grid-angular`](https://www.npmjs.com/package/@inandu-solutions/grid-angular).

One extension method — `ToInanduGrid()` — takes the request an `<inandu-grid serverSide>` makes
(multi-column sort + free-text search + per-column filters + the page or block to show) and returns
**just that page of rows plus the total match count**, ready to hand straight back to the grid.

- **Zero runtime dependencies.** Dynamic sorting/filtering is built on `System.Linq.Expressions` —
  no `System.Linq.Dynamic.Core`, no reflection-based query languages.
- **Works with `IEnumerable<T>` and `IQueryable<T>`.** Over EF Core the sort/filter/paging translate
  to SQL, so the database only returns one page. `ApplyInanduGridQuery` is the `async` escape hatch.
- **Matches the grid's wire contract.** The query-string parser understands the same params
  `@inandu-solutions/grid-pro`'s `createInanduGridDataSource` / `createInanduGridServerRowModel`
  produce (`page`, `pageSize`, `sort=-field`, `field_gte=…`, `q=…`), and `InanduGridColumnFilter`
  mirrors the core `InanduGridColumnFilterValue`.
- **Free / MIT**, published to NuGet.

Multi-targets **`net8.0`** and **`net10.0`**.

### Packages

| Package | For |
|---|---|
| `Inandu.Grid.Extensions` | the core `ToInanduGrid()` — zero dependencies |
| `Inandu.Grid.Extensions.EntityFrameworkCore` | `ToInanduGridAsync()` — `CountAsync` + `ToListAsync` |
| `Inandu.Grid.Extensions.AspNetCore` | `[FromInanduGrid]` model binder + minimal-API binding |

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
the [playground](playground/) for a full working example.

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
dotnet test                                                   # 130 tests, net8.0 + net10.0
dotnet run -c Release --project benchmarks/Inandu.Grid.Extensions.Benchmarks
```

The libraries multi-target `net8.0;net10.0` **when built with the .NET 10 SDK**; with only the
.NET 8 SDK they build `net8.0` alone (see the condition in each `.csproj`). Produce release
packages with the .NET 10 SDK so the `.nupkg`s ship both. See
[docs/publishing.md](docs/publishing.md).

## Contributing

Issues and PRs welcome on the
[Azure DevOps repo](https://inandu.visualstudio.com/DefaultCollection/grid-private/_git/grid-server-side-extensions).
Please add tests for behaviour changes and keep the library dependency-free.

## License

MIT © Inandu SAS — see [LICENSE](LICENSE).
