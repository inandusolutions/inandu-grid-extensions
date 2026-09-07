# Benchmarks

[BenchmarkDotNet](https://benchmarkdotnet.org/) micro-benchmarks for `ToInanduGrid()` and friends
over an in-memory list of **100,000** rows — one benchmark per query shape.

## Run

```bash
dotnet run -c Release --project benchmarks/Inandu.Grid.Extensions.Benchmarks
```

Filter to a subset, or use the fast `short` job while iterating:

```bash
dotnet run -c Release --project benchmarks/Inandu.Grid.Extensions.Benchmarks -- \
  --filter "*Advanced_filter*" "*Grouping*" --job short
```

Results land in `BenchmarkDotNet.Artifacts/` (git-ignored) — a GitHub-flavoured markdown table,
HTML and CSV.

## What's measured

| Benchmark | Query |
|---|---|
| `Page_only` (baseline) | `?page=3&pageSize=25` |
| `Filter_only` | `amount_gte` + `tier_eq` |
| `Sort_only` | `sort=-amount,name` |
| `Filter_sort_page` | filter + multi-sort + deep page |
| `Free_text` | `q=` across the string columns |
| `Advanced_filter` | nested AND/OR JSON tree with `between` |
| `Grouping_top_level` | `groupBy=tier` |
| `Grouping_drill_to_rows` | `groupBy=tier&groupKeys=Pro` |
| `Projection` | filter + sort + page, projected to an anonymous type |
| `Request_parse_and_run` | full query-string parse + everything |

`[MemoryDiagnoser]` is on, so each row also reports allocations — useful for spotting an
expression-tree rebuild that isn't being cached.

Reference numbers (i5-7200U, .NET 8, `short` job, 100k rows): filter+sort+page ≈ 10 ms,
advanced-filter ≈ 4 ms, top-level grouping ≈ 12 ms. Treat these as ballpark — run it on your own
box.
