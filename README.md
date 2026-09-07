# InanduGrid.ServerSide

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

Runs on **.NET 8** and later.

---

## Install

```bash
dotnet add package InanduGrid.ServerSide
```

## Quick start

### ASP.NET Core (in-memory or any `IEnumerable<T>`)

```csharp
using InanduGrid.ServerSide;

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

`ToInanduGrid` on an `IQueryable<T>` runs synchronously (`Count()` + `ToList()`). For `async`, use
`ApplyInanduGridQuery` and run your own `CountAsync` / `ToListAsync`:

```csharp
var query = db.Products.AsNoTracking().ApplyInanduGridQuery(InanduGridOptions.FromQueryString(qs));

var total = await query.FilteredQuery.CountAsync(ct);
var rows  = await query.PagedQuery.ToListAsync(ct);

return query.ToResult(rows, total);
```

### Bind the request yourself (JSON body, gRPC, tests…)

`InanduGridRequest` is a plain DTO. `@inandu-solutions/grid-pro` POSTs a body shaped
`{ sort: [{ field, direction }], page, pageSize, query, columnFilters }` — deserialize it straight
into `InanduGridRequest` (a bundled `System.Text.Json` converter accepts `"asc"` / `"desc"` and the
`"-field"` token form):

```csharp
app.MapPost("/api/products/query", (InanduGridRequest request) =>
    _products.ToInanduGrid(request, o => o.MaxPageSize = 200));
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

## Configuration

See **[docs/options.md](docs/options.md)**. Highlights: `DefaultPageSize`, `MaxPageSize`,
`SearchableFields`, `StringComparison`, `FieldMap` (grid field → CLR path), `ThrowOnUnknownField`,
`Culture`.

## Build & test

```bash
dotnet build
dotnet test
```

## Contributing

Issues and PRs welcome on the
[Azure DevOps repo](https://inandu.visualstudio.com/DefaultCollection/grid-private/_git/grid-server-side-extensions).
Please add tests for behaviour changes and keep the library dependency-free.

## License

MIT © Inandu SAS — see [LICENSE](LICENSE).
