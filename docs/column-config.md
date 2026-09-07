# Per-column configuration & the cost guard

## `options.Column(field)`

A fluent, per-column allow-list. A column left unconfigured keeps the permissive defaults.

```csharp
_products.ToInanduGrid(qs, o =>
{
    o.Column("customer").Path("Customer.Name").Label("Customer").Searchable();
    o.Column("price").Filterable(FilterOperator.GreaterThanOrEqual, FilterOperator.LessThanOrEqual);
    o.Column("sku").Filterable(FilterOperator.Equal, FilterOperator.StartsWith);
    o.Column("internalNote").NotFilterable().Sortable(false);
    o.Column("legacyId").Sortable(false);
});
```

| Method | Effect |
|---|---|
| `.Path("Clr.Path")` | Resolve this grid field to a CLR property path (wins over `FieldMap`). |
| `.Filterable(ops…)` | Restrict filtering to these operators. No args ⇒ any operator. |
| `.NotFilterable()` | Disallow filtering entirely. |
| `.Sortable(bool)` | Allow / disallow sorting. |
| `.Searchable(bool)` | Force this column in / out of free-text search. |
| `.Label("…")` | Human label used in error messages. |

`SearchableFields` and the per-column `Searchable(...)` overrides combine: columns forced *in* are
added, columns forced *out* are removed.

**What happens to a disallowed request bit:** by default it's silently dropped (a stale client
can't break the endpoint). Set `o.ThrowOnUnknownField = true` to reject instead — an
`InanduGridRequestException` whose `Errors` map is shaped like `ValidationProblemDetails`:

```csharp
catch (InanduGridRequestException ex)
{
    return Results.ValidationProblem(ex.Errors); // { "filter.price": ["Filtering 'price' with 'eq' is not allowed."] }
}
```

## `options.Limits` — the cost guard

Caps on how large / deep an incoming request may be. `options.OnLimitExceeded` decides what happens
when a cap is hit: **`Trim`** (default — quietly cut down to the cap) or **`Reject`**
(`InanduGridRequestException`).

| Limit | Default |
|---|---|
| `MaxConditions` | 50 |
| `MaxSortColumns` | 5 |
| `MaxAdvancedFilterNodes` | 200 |
| `MaxAdvancedFilterDepth` | 10 |
| `MaxInListItems` | 500 |
| `MaxGroupByLevels` | 5 |
| `MaxAggregations` | 20 |

```csharp
o.Limits.MaxConditions = 20;
o.Limits.MaxInListItems = 100;
o.OnLimitExceeded = InanduGridLimitMode.Reject;
```

The guard runs once, at the top of every entry point (`ToInanduGrid`, `ToInanduGridGrouped`,
`ToInanduGridDistinct`, `ApplyInanduGridQuery`, and the EF `…Async` variants). In `Trim` mode it
mutates the bound `InanduGridRequest` in place.
