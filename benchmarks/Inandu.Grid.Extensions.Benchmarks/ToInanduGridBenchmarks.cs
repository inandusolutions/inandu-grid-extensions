using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using Inandu.Grid.Extensions;

namespace Inandu.Grid.Extensions.Benchmarks;

public enum Tier
{
    Free = 0,
    Pro = 1,
    Enterprise = 2,
}

public sealed class Row
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public decimal Amount { get; set; }
    public int? Quantity { get; set; }
    public bool Flagged { get; set; }
    public Tier Tier { get; set; }
    public DateTime CreatedOn { get; set; }
}

[MemoryDiagnoser]
[MarkdownExporterAttribute.GitHub]
public class ToInanduGridBenchmarks
{
    private List<Row> _rows = new();

    [Params(100_000)]
    public int N;

    private static readonly string AdvancedFilterJson = """
        {"kind":"group","combinator":"and","children":[
          {"kind":"condition","field":"tier","operator":"eq","value":"Pro"},
          {"kind":"group","combinator":"or","children":[
            {"kind":"condition","field":"amount","operator":"gte","value":500},
            {"kind":"condition","field":"quantity","operator":"between","value":10,"value2":50}
          ]}
        ]}
        """;

    [GlobalSetup]
    public void Setup()
    {
        var rnd = new Random(1234);
        var cats = new[] { "alpha", "beta", "gamma", "delta", "epsilon" };
        _rows = Enumerable.Range(1, N).Select(i => new Row
        {
            Id = i,
            Name = $"Row {i:D6}",
            Category = cats[i % cats.Length],
            Notes = i % 5 == 0 ? null : $"note {i}",
            Amount = Math.Round((decimal)(rnd.NextDouble() * 1000), 2),
            Quantity = i % 11 == 0 ? null : rnd.Next(0, 100),
            Flagged = i % 7 == 0,
            Tier = (Tier)(i % 3),
            CreatedOn = new DateTime(2026, 1, 1).AddMinutes(i),
        }).ToList();
    }

    private static InanduGridOptions Q(string queryString) => InanduGridOptions.FromQueryString(queryString);

    [Benchmark(Baseline = true)]
    public int Page_only() => _rows.ToInanduGrid(Q("?page=3&pageSize=25")).Data.Count;

    [Benchmark]
    public int Filter_only() => _rows.ToInanduGrid(Q("?pageSize=25&amount_gte=500&tier_eq=Pro")).Total;

    [Benchmark]
    public int Sort_only() => _rows.ToInanduGrid(Q("?pageSize=25&sort=-amount,name")).Data.Count;

    [Benchmark]
    public int Filter_sort_page() => _rows.ToInanduGrid(Q("?page=5&pageSize=25&sort=-createdOn&amount_gte=200&flagged_eq=false")).Data.Count;

    [Benchmark]
    public int Free_text() => _rows.ToInanduGrid(Q("?pageSize=25&q=00042")).Total;

    [Benchmark]
    public int Advanced_filter() => _rows.ToInanduGrid(Q("?pageSize=25&filter=" + Uri.EscapeDataString(AdvancedFilterJson))).Total;

    [Benchmark]
    public int Grouping_top_level() => _rows.ToInanduGridGrouped(Q("?groupBy=tier&pageSize=25")).Groups!.Count;

    [Benchmark]
    public int Grouping_drill_to_rows() => _rows.ToInanduGridGrouped(Q("?groupBy=tier&groupKeys=Pro&pageSize=25")).Rows!.Count;

    [Benchmark]
    public int Projection() =>
        _rows.ToInanduGrid(r => new { r.Id, r.Name, r.Amount }, Q("?page=3&pageSize=25&sort=-amount&tier_eq=Pro")).Data.Count;

    [Benchmark]
    public int Request_parse_and_run() =>
        _rows.ToInanduGrid("?page=2&pageSize=50&sort=-amount,name&amount_gte=100&amount_lte=900&category_in=alpha,beta&q=note").Data.Count;
}
