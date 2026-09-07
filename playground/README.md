# Playground

A real `<inandu-grid serverSide>` talking to an ASP.NET Core API that pages, sorts and filters an
in-memory catalogue of 10,000 products with a single `ToInanduGrid()` call.

```
playground/
├── InanduGrid.ServerSide.Playground/   ASP.NET Core minimal API  (GET /api/products, Swagger)
└── web/                                Angular front end (standalone <inandu-grid>), bundled with esbuild
```

## Run it

1. **Build the front end** (needs Node 18+):

   ```bash
   cd playground/web
   npm install
   npm run build          # → ../InanduGrid.ServerSide.Playground/wwwroot/bundle.js
   ```

   Use `npm run watch` while hacking on `main.ts`.

2. **Run the API** (needs the .NET 8 SDK):

   ```bash
   cd playground/InanduGrid.ServerSide.Playground
   dotnet run
   ```

3. Open <http://localhost:5238>. Swagger UI is at <http://localhost:5238/swagger>.

## What to look at

- **`Program.cs`** — the entire server side is one line:

  ```csharp
  app.MapGet("/api/products", (HttpRequest request) =>
      Results.Ok(ProductStore.All.ToInanduGrid(request.QueryString.Value, o =>
      {
          o.SearchableFields = new() { "Name", "Sku", "Category" };
          o.MaxPageSize = 200;
      })));
  ```

- **`web/main.ts`** — turns `(sortChange)` / `(pageChange)` / `(filterChange)` into the REST query
  string (`page`, `pageSize`, `sort=-price`, `price_gte=20`, `q=…`) and binds the JSON response's
  `data` / `total` back onto `[data]` / `[totalItems]`.
- The **Network tab**: every grid interaction is one `GET /api/products?…` and the response is only
  the visible page.
