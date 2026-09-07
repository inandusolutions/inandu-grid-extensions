# Advanced filter (nested AND / OR)

`@inandu-solutions/grid-pro`'s advanced-filter query builder produces a **tree** of AND / OR groups
whose leaves are `{ field, operator, value }` conditions. Client-side it feeds
`[extraRowFilter]`; server-side, `advancedQueryToRestParams(query)` serialises it to a single
`filter=<json>` param — and this library parses and applies it.

## Wire shape

```json
{
  "kind": "group",
  "combinator": "and",
  "children": [
    { "kind": "condition", "field": "status", "operator": "eq", "value": "Active" },
    { "kind": "group", "combinator": "or", "children": [
      { "kind": "condition", "field": "price", "operator": "gt", "value": 100 },
      { "kind": "condition", "field": "stock", "operator": "between", "value": 0, "value2": 30 }
    ]}
  ]
}
```

## Operators

| Token | Meaning |
|---|---|
| `eq` `neq` | equals / not equals |
| `contains` `notContains` | substring / negated (strings) |
| `startsWith` `endsWith` | prefix / suffix |
| `gt` `gte` `lt` `lte` | ordered comparison |
| `between` | `value` ≤ x ≤ `value2` |
| `isTrue` `isFalse` | boolean columns |
| `isEmpty` `isNotEmpty` | null, or empty string |

## Using it

The parsing is automatic when the request comes through `InanduGridRequest.Parse` (query string /
`InanduGridBinding`) or when a JSON body carries a `"filter"` string field:

```csharp
var result = _products.ToInanduGrid(Request.QueryString.Value); // filter= already handled
```

By hand:

```csharp
var request = new InanduGridRequest { Filter = json };           // parses on set
// or
request.AdvancedFilter = AdvancedFilterJson.Parse(json);         // AdvancedFilterGroup?
```

`AdvancedFilterJson.Parse` returns `null` for blank / malformed input — a bad param is a no-op,
never a 400. The tree is applied as an **extra `AND`** on top of `ColumnFilters` / flat conditions
/ free-text. Unknown fields are skipped (or throw when `ThrowOnUnknownField` is set). Values are
coerced to the column's CLR type like every other filter; `Date` operands sent as ISO strings work.

Ordered comparison (`gt`/`gte`/`lt`/`lte`, and therefore `between`) is not emitted for `bool` /
`Guid` / `enum` members — use `eq` / `in` there.
