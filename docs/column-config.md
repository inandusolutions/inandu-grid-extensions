# Per-column configuration & the cost guard

## `options.Column(field)`

A fluent, per-column allow-list. A column left unconfigured keeps the permissive defaults —
unless `o.AllowUnconfiguredFields = false`, in which case **only** configured columns are honoured.

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
| `.NotFilterable()` | Disallow filtering entirely — and grouping / distinct values, which expose the same values. |
| `.Sortable(bool)` | Allow / disallow sorting. |
| `.Searchable(bool)` | Force this column in / out of free-text search. |
| `.NoCursor()` | Keep this column out of keyset cursors: a sort that includes it is paged by offset. See [keyset-pagination](keyset-pagination.md#protecting-the-cursor). |
| `.Label("…")` | Human label used in error messages. |

**Grouping, distinct values and aggregates follow `CanFilter`.** `groupBy`, `ToInanduGridDistinct`
and `aggregate=min:/max:/sum:/avg:/count:<field>` reveal a column's values just as a filter does, so a `.NotFilterable()` column can't be grouped or listed:
the `groupBy` level (and its `groupKeys` entry) is dropped, a distinct request returns no values,
and the aggregate is left out.
An unknown group / distinct field is handled the same way, instead of throwing `ArgumentException`.

### Deny by default — `o.AllowUnconfiguredFields = false`

By default any public property of the row type — including dotted navigation paths like
`customer.email` — can be filtered, sorted, grouped, listed and aggregated, and free-text search
covers every public `string` property. That's convenient for internal tools, but on a public
endpoint it lets a client probe fields you never meant to expose (watch `total` change while
guessing `passwordHash_startsWith=…`). This applies to the `ToInanduGrid(selector, …)` overloads
too: they filter on the *source* entity, not the DTO.

With `AllowUnconfiguredFields = false`, a field with no `o.Column(...)` entry is treated like a
`.NotFilterable().Sortable(false)` column (dropped, or rejected with `ThrowOnUnknownField`), and
free-text search covers only `SearchableFields` plus columns marked `.Searchable()`. A `FieldMap`
entry alone doesn't count as configured — use `o.Column("x").Path("X.Y")`.

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
| `MaxQueryTextLength` | 200 |
| `MaxFilterLength` | 20000 |
| `MaxQueryStringLength` | 32768 |
| `MaxCursorLength` | 4096 |
| `MaxValueLength` | 1000 |
| `MaxFieldNameLength` | 128 |

```csharp
o.Limits.MaxConditions = 20;
o.Limits.MaxInListItems = 100;
o.OnLimitExceeded = InanduGridLimitMode.Reject;
```

The guard runs once, at the top of every entry point (`ToInanduGrid`, `ToInanduGridGrouped`,
`ToInanduGridDistinct`, `ApplyInanduGridQuery`, and the EF `…Async` variants). In `Trim` mode it
mutates the bound `InanduGridRequest` in place.
