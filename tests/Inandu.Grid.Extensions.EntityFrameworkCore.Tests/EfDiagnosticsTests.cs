using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inandu.Grid.Extensions.EntityFrameworkCore.Tests;

/// <summary>The async entry points report through the same source as the sync ones.</summary>
public class EfDiagnosticsTests : IClassFixture<ShopFixture>
{
    private static readonly AsyncLocal<bool> Active = new();
    private readonly ShopFixture _fx;

    public EfDiagnosticsTests(ShopFixture fx) => _fx = fx;

    [Fact]
    public async Task Async_queries_emit_an_activity_per_operation()
    {
        var seen = new List<Activity>();
        var gate = new object();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == InanduGridDiagnostics.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => { if (Active.Value) { lock (gate) { seen.Add(a); } } },
        };
        ActivitySource.AddActivityListener(listener);
        Active.Value = true;

        await using var db = _fx.NewContext();
        var page = await db.Widgets.AsNoTracking().ToInanduGridAsync("?pageSize=5");
        await db.Widgets.AsNoTracking().ToInanduGridGroupedAsync("?groupBy=category&pageSize=5");
        await db.Widgets.AsNoTracking().ToInanduGridDistinctAsync("category", "?pageSize=5");

        Assert.Equal(
            new[] { "inandu.grid.query", "inandu.grid.grouped", "inandu.grid.distinct" },
            seen.Select(a => a.OperationName).ToArray());
        Assert.Equal(page.Total, seen[0].GetTagItem("inandu.grid.total"));
        Active.Value = false;
    }
}
