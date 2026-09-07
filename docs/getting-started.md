# Getting started

## 1. Install

```bash
dotnet add package InanduGrid.ServerSide
```

Multi-targets `net8.0` and `net10.0`.

## 2. Expose an endpoint

```csharp
using Inandu.Grid.Extensions;

// minimal API
app.MapGet("/api/orders", (HttpRequest req) =>
    db.Orders.AsNoTracking().ToInanduGrid(req.QueryString.Value, o =>
    {
        o.SearchableFields = new() { "Customer", "Reference" };
        o.MaxPageSize = 200;
        o.FieldMap = new Dictionary<string, string> { ["customer"] = "Customer.Name" };
    }));
```

The response is `{ data, total, page, pageSize, pageCount }` in camelCase.

## 3. Point the grid at it

With `@inandu-solutions/grid-pro`:

```ts
readonly ds = createInanduGridDataSource<Order>({
  dialect: 'rest',
  url: '/api/orders',
  map: (j: any) => ({ rows: j.data, total: j.total }),
});
```

```html
<inandu-grid serverSide filter="true"
  [data]="ds.rows()" [totalItems]="ds.total()" [loading]="ds.loading()" [error]="ds.error()"
  (sortChange)="ds.onSort($event)" (pageChange)="ds.onPage($event)" (filterChange)="ds.onFilter($event)">
  <inandu-column field="reference" title="Ref" sortable="true" filter="true" />
  <inandu-column field="customer" title="Customer" sortable="true" filter="true" />
  <inandu-column field="total" title="Total" type="number" sortable="true" filter="true" />
  <inandu-column field="placedOn" title="Placed" type="date" sortable="true" filter="true" />
</inandu-grid>
```

Or without grid-pro, build the query string from the three `serverSide` events yourself — see
[`playground/web/main.ts`](../playground/web/main.ts).

## 4. Async with EF Core

```csharp
app.MapGet("/api/orders", async (HttpRequest req, AppDb db, CancellationToken ct) =>
{
    var q = db.Orders.AsNoTracking().ApplyInanduGridQuery(InanduGridOptions.FromQueryString(req.QueryString.Value));
    var total = await q.FilteredQuery.CountAsync(ct);
    var rows  = await q.PagedQuery.ToListAsync(ct);
    return Results.Ok(q.ToResult(rows, total));
});
```

## Next

- [Request contract](request-contract.md) — every parameter.
- [Options](options.md) — every knob.
- [Playground](../playground/README.md) — a runnable end-to-end example.
