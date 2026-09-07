# InanduGridOptions

Everything `ToInanduGrid()` needs: the bound `Request` plus how to apply it.

| Property | Default | Purpose |
|---|---|---|
| `Request` | empty (page 1) | The sort + filter + paging to apply. Set it, or use a factory below. |
| `DefaultPageSize` | `25` | Page size when the request doesn't specify one. |
| `MaxPageSize` | `500` | Hard cap — a client asking for more is clamped. |
| `SearchableFields` | `null` | Fields the `q` box searches. `null` ⇒ every `string` property; empty list ⇒ disabled. Names are case-insensitive and may be dotted paths. |
| `StringComparison` | `OrdinalIgnoreCase` | Case handling for `contains`/`startsWith`/`endsWith`/`eq` on strings and for `q`. **In-memory only** — over `IQueryable`/EF the database collation decides. Case-insensitive modes lower both operands (`ToLower()`), which EF can translate. |
| `FieldMap` | `null` | Grid `field` → CLR property path, e.g. `{ ["customerName"] = "Customer.Name" }`. Unmapped fields pass through. |
| `ThrowOnUnknownField` | `false` | `false`: an unknown sort/filter field is silently skipped (a stale client can't break the endpoint). `true`: it throws `ArgumentException`. |
| `Culture` | `InvariantCulture` | Used to parse numeric / date operands. |

## Factories

```csharp
// from a raw query string
InanduGridOptions.FromQueryString(Request.QueryString.Value, o => o.MaxPageSize = 100);

// from a key/value collection (e.g. Request.Query flattened)
InanduGridOptions.FromQuery(pairs, configure);

// wrap an already-bound request (JSON body, tests, gRPC…)
InanduGridOptions.For(request, configure);
```

All three take an optional `Action<InanduGridOptions>` so you configure in one expression.

## Extension methods

| Method | Returns | Use when |
|---|---|---|
| `IEnumerable<T>.ToInanduGrid(options?)` | `InanduGridResult<T>` | In-memory list / array. |
| `IQueryable<T>.ToInanduGrid(options?)` | `InanduGridResult<T>` | EF Core etc., synchronous (`Count()` + `ToList()`). |
| `…ToInanduGrid(InanduGridRequest, configure?)` | `InanduGridResult<T>` | You already bound the request. |
| `…ToInanduGrid(string queryString, configure?)` | `InanduGridResult<T>` | One-liner from a query string. |
| `IQueryable<T>.ApplyInanduGridQuery(options?)` | `InanduGridQuery<T>` | You need `async` — call `CountAsync` on `FilteredQuery`, `ToListAsync` on `PagedQuery`, then `query.ToResult(rows, total)`. |
