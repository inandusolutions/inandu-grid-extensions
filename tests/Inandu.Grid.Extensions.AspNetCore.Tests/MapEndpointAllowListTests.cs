using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Inandu.Grid.Extensions.AspNetCore.Tests;

/// <summary>
/// End-to-end over HTTP: a locked column can't be read through the distinct, grouped or aggregate
/// endpoints, and an unknown field no longer turns into a 500.
/// </summary>
public class MapEndpointAllowListTests
{
    private sealed record Account(int Id, string Name, string Region, string Secret, int Balance);

    private static readonly List<Account> Data = Enumerable.Range(1, 30)
        .Select(i => new Account(i, $"Account {i:D2}", $"R{i % 3}", $"s3cr3t-{i}", i * 10))
        .ToList();

    private static void Lock(InanduGridOptions o) => o.Column("secret").NotFilterable();

    private static async Task<HttpClient> ServerAsync()
    {
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e =>
                {
                    e.MapInanduGrid("/accounts", Data, Lock);
                    e.MapInanduGridGrouped("/accounts/groups", Data, Lock);
                    e.MapInanduGridDistinct("/accounts/distinct/{field}", Data, configure: Lock);
                });
            });
            web.ConfigureServices(s => s.AddRouting());
        });

        var host = await builder.StartAsync();
        return host.GetTestClient();
    }

    [Fact]
    public async Task Distinct_on_a_locked_column_returns_no_values()
    {
        var client = await ServerAsync();
        var doc = await client.GetFromJsonAsync<JsonElement>("/accounts/distinct/secret?pageSize=100");

        Assert.Equal(0, doc.GetProperty("values").GetArrayLength());
        Assert.Equal(0, doc.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Distinct_on_a_locked_column_ignores_casing_tricks()
    {
        var client = await ServerAsync();
        var doc = await client.GetFromJsonAsync<JsonElement>("/accounts/distinct/SECRET?pageSize=100");

        Assert.Equal(0, doc.GetProperty("values").GetArrayLength());
    }

    [Fact]
    public async Task Distinct_on_an_allowed_column_still_works()
    {
        var client = await ServerAsync();
        var doc = await client.GetFromJsonAsync<JsonElement>("/accounts/distinct/region?pageSize=100");

        Assert.Equal(3, doc.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task Distinct_on_an_unknown_field_is_not_a_500()
    {
        var client = await ServerAsync();
        var response = await client.GetAsync("/accounts/distinct/doesNotExist");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Grouping_by_a_locked_column_returns_rows_not_groups()
    {
        var client = await ServerAsync();
        var doc = await client.GetFromJsonAsync<JsonElement>("/accounts/groups?groupBy=secret&pageSize=5");

        Assert.True(doc.GetProperty("isLeaf").GetBoolean());
        Assert.Equal(JsonValueKind.Null, doc.TryGetProperty("groups", out var g) ? g.ValueKind : JsonValueKind.Null);
    }

    [Fact]
    public async Task Grouping_by_an_unknown_field_is_not_a_500()
    {
        var client = await ServerAsync();
        var response = await client.GetAsync("/accounts/groups?groupBy=doesNotExist");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Aggregates_do_not_reveal_a_locked_column()
    {
        var client = await ServerAsync();
        var doc = await client.GetFromJsonAsync<JsonElement>("/accounts?aggregate=max:secret,min:secret,sum:balance&pageSize=1");

        var aggregations = doc.GetProperty("aggregations");
        Assert.False(aggregations.TryGetProperty("max:secret", out _));
        Assert.False(aggregations.TryGetProperty("min:secret", out _));
        Assert.Equal(Data.Sum(a => a.Balance), aggregations.GetProperty("sum:balance").GetInt32());
    }
}
