using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Inandu.Grid.Extensions.EntityFrameworkCore.Tests;

public enum ShopStatus
{
    Draft = 0,
    Active = 1,
    Archived = 2,
}

public sealed class Widget
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int? Stock { get; set; }
    public bool Discontinued { get; set; }
    public ShopStatus Status { get; set; }
    public DateTime CreatedOn { get; set; }
}

/// <summary>Group keys that need care: a comma, an empty value, edge whitespace.</summary>
public sealed class Contact
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
}

public sealed class ShopContext : DbContext
{
    public ShopContext(DbContextOptions<ShopContext> options) : base(options) { }

    public DbSet<Widget> Widgets => Set<Widget>();

    public DbSet<Contact> Contacts => Set<Contact>();

    // The seed assigns ids itself; without this SQL Server makes `Id` an IDENTITY column and rejects them.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>().Property(w => w.Id).ValueGeneratedNever();
        modelBuilder.Entity<Contact>().Property(c => c.Id).ValueGeneratedNever();
    }
}

/// <summary>
/// One shared database, seeded once. SQLite in memory by default; set <c>INANDU_TEST_PROVIDER</c> to
/// <c>sqlserver</c> or <c>postgres</c> and <c>INANDU_TEST_CONNECTION</c> to a server-level connection
/// string (a user that can create databases) to run the same tests against a real server. Each fixture
/// creates its own throw-away database and drops it on dispose.
/// </summary>
public sealed class ShopFixture : IDisposable
{
    private readonly DbConnection? _connection;
    private readonly Action? _cleanup;

    public ShopFixture()
    {
        var provider = (Environment.GetEnvironmentVariable("INANDU_TEST_PROVIDER") ?? "sqlite").ToLowerInvariant();
        var server = Environment.GetEnvironmentVariable("INANDU_TEST_CONNECTION");
        var dbName = "inandu_" + Guid.NewGuid().ToString("N");

        switch (provider)
        {
            case "sqlite":
                _connection = new SqliteConnection("DataSource=:memory:");
                _connection.Open();
                OptionsBuilder = new DbContextOptionsBuilder<ShopContext>().UseSqlite(_connection).Options;
                break;

            case "sqlserver":
            {
                var admin = RequireConnection(server, provider);
                Execute(() => new Microsoft.Data.SqlClient.SqlConnection(admin), $"CREATE DATABASE [{dbName}]");
                var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(admin) { InitialCatalog = dbName };
                OptionsBuilder = new DbContextOptionsBuilder<ShopContext>().UseSqlServer(builder.ConnectionString).Options;
                _cleanup = () =>
                {
                    Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
                    Execute(() => new Microsoft.Data.SqlClient.SqlConnection(admin), $"DROP DATABASE [{dbName}]");
                };
                break;
            }

            case "postgres":
            {
                // Seed timestamps are DateTimeKind.Unspecified; keep Npgsql's pre-6 behaviour so they map to `timestamp`.
                AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
                var admin = RequireConnection(server, provider);
                Execute(() => new Npgsql.NpgsqlConnection(admin), $"CREATE DATABASE \"{dbName}\"");
                var builder = new Npgsql.NpgsqlConnectionStringBuilder(admin) { Database = dbName };
                OptionsBuilder = new DbContextOptionsBuilder<ShopContext>().UseNpgsql(builder.ConnectionString).Options;
                _cleanup = () =>
                {
                    Npgsql.NpgsqlConnection.ClearAllPools();
                    Execute(() => new Npgsql.NpgsqlConnection(admin), $"DROP DATABASE \"{dbName}\" WITH (FORCE)");
                };
                break;
            }

            default:
                throw new InvalidOperationException($"Unknown INANDU_TEST_PROVIDER '{provider}' (sqlite, sqlserver, postgres).");
        }

        using var db = new ShopContext(OptionsBuilder);
        db.Database.EnsureCreated();

        db.Widgets.AddRange(Enumerable.Range(1, 240).Select(i => new Widget
        {
            Id = i,
            Name = $"Widget {i:D4}",
            Category = $"Cat {i % 6}",
            Price = 5m + (i % 100),
            Stock = i % 13 == 0 ? null : i % 400,
            Discontinued = i % 9 == 0,
            Status = (ShopStatus)(i % 3),
            CreatedOn = new DateTime(2026, 1, 1).AddHours(i),
        }));
        db.Contacts.AddRange(
            new Contact { Id = 1, Name = "Smith, John", Region = "EMEA" },
            new Contact { Id = 2, Name = "Smith, John", Region = "APAC" },
            new Contact { Id = 3, Name = "Smith", Region = "EMEA" },
            new Contact { Id = 4, Name = "", Region = "EMEA" },
            new Contact { Id = 5, Name = " padded", Region = "EMEA" });
        db.SaveChanges();
    }

    public DbContextOptions<ShopContext> OptionsBuilder { get; }

    public ShopContext NewContext() => new(OptionsBuilder);

    public void Dispose()
    {
        _connection?.Dispose();
        _cleanup?.Invoke();
    }

    private static string RequireConnection(string? connection, string provider)
        => string.IsNullOrWhiteSpace(connection)
            ? throw new InvalidOperationException($"INANDU_TEST_CONNECTION must be set to run against '{provider}'.")
            : connection;

    private static void Execute(Func<DbConnection> open, string sql)
    {
        using var connection = open();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

public class EfCoreTests : IClassFixture<ShopFixture>
{
    private readonly ShopFixture _fx;

    public EfCoreTests(ShopFixture fx) => _fx = fx;

    [Fact]
    public async Task ToInanduGridAsync_pages_sorts_filters_via_sql()
    {
        await using var db = _fx.NewContext();

        // NB: SQLite can't ORDER BY decimal, so sort on a non-decimal column here.
        var result = await db.Widgets.AsNoTracking()
            .ToInanduGridAsync("?page=2&pageSize=10&sort=-id&price_gte=50&category_eq=Cat 1", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Page);
        Assert.InRange(result.Data.Count, 1, 10);
        Assert.All(result.Data, w => Assert.True(w.Price >= 50m && w.Category == "Cat 1"));
        var ids = result.Data.Select(w => w.Id).ToList();
        Assert.Equal(ids.OrderByDescending(i => i).ToList(), ids);
        Assert.True(result.Total > 10, "the filter should match more than one page");
    }

    [Fact]
    public async Task ToInanduGridAsync_total_reflects_the_filter()
    {
        await using var db = _fx.NewContext();

        var request = new InanduGridRequest { PageSize = 5 };
        request.ColumnFilters["status"] = new InanduGridColumnFilter { Values = new() { "Active" } };

        var result = await db.Widgets.AsNoTracking().ToInanduGridAsync(request, cancellationToken: TestContext.Current.CancellationToken);

        var expected = await db.Widgets.CountAsync(w => w.Status == ShopStatus.Active, TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Total);
        Assert.Equal(5, result.Data.Count);
    }

    [Fact]
    public async Task ToInanduGridAsync_projection_only_reads_the_dto_columns()
    {
        await using var db = _fx.NewContext();

        var result = await db.Widgets.AsNoTracking().ToInanduGridAsync(
            w => new { w.Id, w.Name },
            "?pageSize=5&sort=id", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, result.Data.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, result.Data.Select(x => x.Id));
    }

    [Fact]
    public async Task ToInanduGridAsync_advanced_filter_tree()
    {
        await using var db = _fx.NewContext();

        var json = """
            {"kind":"group","combinator":"and","children":[
              {"kind":"condition","field":"status","operator":"eq","value":"Active"},
              {"kind":"group","combinator":"or","children":[
                {"kind":"condition","field":"price","operator":"gte","value":90},
                {"kind":"condition","field":"stock","operator":"isEmpty"}
              ]}
            ]}
            """;

        var result = await db.Widgets.AsNoTracking()
            .ToInanduGridAsync("?pageSize=200&filter=" + Uri.EscapeDataString(json), cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(result.Data, w =>
        {
            Assert.Equal(ShopStatus.Active, w.Status);
            Assert.True(w.Price >= 90m || w.Stock is null);
        });
        Assert.NotEmpty(result.Data);
    }

    [Fact]
    public async Task ToInanduGridGroupedAsync_top_level_then_drill()
    {
        await using var db = _fx.NewContext();

        var top = await db.Widgets.AsNoTracking().ToInanduGridGroupedAsync("?groupBy=category&pageSize=50", cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(top.IsLeaf);
        Assert.Equal(6, top.Total); // Cat 0..5
        Assert.Equal(240, top.Groups!.Sum(g => g.Count));

        var drill = await db.Widgets.AsNoTracking().ToInanduGridGroupedAsync("?groupBy=category&groupKeys=Cat 1&pageSize=1000", cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(drill.IsLeaf);
        Assert.All(drill.Rows!, w => Assert.Equal("Cat 1", w.Category));
        Assert.Equal(40, drill.Total);
    }

    [Fact]
    public async Task ToInanduGridGroupedAsync_orders_groups_by_count_desc()
    {
        await using var db = _fx.NewContext();

        var res = await db.Widgets.AsNoTracking()
            .ToInanduGridGroupedAsync("?groupBy=category&sort=-count&discontinued_eq=false&pageSize=50", cancellationToken: TestContext.Current.CancellationToken);

        var counts = res.Groups!.Select(g => g.Count).ToList();
        Assert.Equal(counts.OrderByDescending(c => c).ToList(), counts);
    }

    [Fact]
    public async Task ToInanduGridAsync_computes_aggregations_over_the_filter()
    {
        await using var db = _fx.NewContext();

        // NB: SQLite can't SUM/AVG/MAX/MIN decimal, so aggregate int / date columns here.
        var result = await db.Widgets.AsNoTracking()
            .ToInanduGridAsync("?pageSize=2&status_eq=Active&aggregate=sum:stock,count:*,max:stock,min:createdOn", cancellationToken: TestContext.Current.CancellationToken);

        var active = db.Widgets.Where(w => w.Status == ShopStatus.Active);
        Assert.Equal(await active.SumAsync(w => w.Stock, TestContext.Current.CancellationToken), (int?)result.Aggregations!["sum:stock"]);
        Assert.Equal(await active.CountAsync(TestContext.Current.CancellationToken), result.Aggregations["count:*"]);
        Assert.Equal(await active.MaxAsync(w => w.Stock, TestContext.Current.CancellationToken), (int?)result.Aggregations["max:stock"]);
        Assert.IsType<DateTime>(result.Aggregations["min:createdOn"]);
    }

    [Fact]
    public async Task ToInanduGridAsync_keyset_pages_forward_without_overlap()
    {
        await using var db = _fx.NewContext();

        var page1 = await db.Widgets.AsNoTracking().ToInanduGridAsync("?pageSize=20&sort=id", o => o.EnableKeyset = true, cancellationToken: TestContext.Current.CancellationToken);
        var page2 = await db.Widgets.AsNoTracking().ToInanduGridAsync($"?pageSize=20&sort=id&after={page1.NextCursor}", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(Enumerable.Range(1, 20), page1.Data.Select(w => w.Id));
        Assert.Equal(Enumerable.Range(21, 20), page2.Data.Select(w => w.Id));
        Assert.True(page1.HasMore);
    }

    [Fact]
    public async Task ToInanduGridDistinctAsync_values_and_counts()
    {
        await using var db = _fx.NewContext();

        var result = await db.Widgets.AsNoTracking().ToInanduGridDistinctAsync("category", "?pageSize=50", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(6, result.Total);
        Assert.Equal(240, result.Values.Sum(v => v.Count));
    }

    [Fact]
    public async Task ToInanduGridDistinctAsync_honours_NotFilterable()
    {
        await using var db = _fx.NewContext();

        var result = await db.Widgets.AsNoTracking().ToInanduGridDistinctAsync("category", "?pageSize=50",
            o => o.Column("category").NotFilterable(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Values);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task ToInanduGridGroupedAsync_drops_a_NotFilterable_groupBy()
    {
        await using var db = _fx.NewContext();

        var result = await db.Widgets.AsNoTracking().ToInanduGridGroupedAsync("?groupBy=category",
            o => o.Column("category").NotFilterable(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsLeaf);
        Assert.Equal(240, result.Total);
    }
}
