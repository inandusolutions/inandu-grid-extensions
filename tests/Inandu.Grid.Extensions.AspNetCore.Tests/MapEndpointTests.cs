using System;
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

public class MapEndpointTests
{
    private sealed record Item(int Id, string Name, string Group, int Score);

    private static readonly List<Item> Data = Enumerable.Range(1, 60)
        .Select(i => new Item(i, $"Item {i:D3}", $"G{i % 4}", i * 3 % 100))
        .ToList();

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
                    e.MapInanduGrid("/items", Data);
                    e.MapInanduGridGrouped("/items/groups", Data);
                    e.MapInanduGridDistinct("/items/distinct/{field}", Data);
                    e.MapInanduGrid("/strict", Data, o => o.ThrowOnUnknownField = true);
                    e.MapInanduGridGrouped("/strict/groups", Data, o => o.ThrowOnUnknownField = true);
                    e.MapInanduGrid("/capped", Data, o =>
                    {
                        o.Limits.MaxSortColumns = 1;
                        o.OnLimitExceeded = InanduGridLimitMode.Reject;
                    });
                });
            });
            web.ConfigureServices(s => s.AddRouting());
        });

        var host = await builder.StartAsync();
        return host.GetTestClient();
    }

    [Fact]
    public async Task MapInanduGrid_returns_a_page()
    {
        var client = await ServerAsync();
        var doc = await client.GetFromJsonAsync<JsonElement>("/items?pageSize=5&sort=-id");

        Assert.Equal(5, doc.GetProperty("data").GetArrayLength());
        Assert.Equal(60, doc.GetProperty("total").GetInt32());
        Assert.Equal(60, doc.GetProperty("data")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task MapInanduGridGrouped_returns_groups()
    {
        var client = await ServerAsync();
        var doc = await client.GetFromJsonAsync<JsonElement>("/items/groups?groupBy=group&pageSize=10");

        Assert.False(doc.GetProperty("isLeaf").GetBoolean());
        Assert.Equal(4, doc.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task MapInanduGridDistinct_uses_the_route_field()
    {
        var client = await ServerAsync();
        var doc = await client.GetFromJsonAsync<JsonElement>("/items/distinct/group?pageSize=10");

        Assert.Equal("group", doc.GetProperty("field").GetString());
        Assert.Equal(4, doc.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task MapInanduGridDistinct_400_without_a_field()
    {
        var client = await ServerAsync();
        // a pattern needing {field} but hit without one isn't routable; test the query-param fallback path
        var builder = new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapInanduGridDistinct("/d", Data));
            });
            web.ConfigureServices(s => s.AddRouting());
        });
        using var host = await builder.StartAsync();
        var resp = await host.GetTestClient().GetAsync("/d");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var doc = await resp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("errors").TryGetProperty("field", out _));
    }

    [Fact]
    public async Task MapInanduGrid_returns_400_validation_problem_for_an_unknown_field_in_strict_mode()
    {
        var client = await ServerAsync();
        var response = await client.GetAsync("/strict?sort=nope");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("errors").TryGetProperty("sort", out _));
    }

    [Fact]
    public async Task MapInanduGrid_returns_400_validation_problem_when_a_limit_is_rejected()
    {
        var client = await ServerAsync();
        var response = await client.GetAsync("/capped?sort=id,name");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(doc.GetProperty("errors").TryGetProperty("sort", out _));
    }

    [Fact]
    public async Task MapInanduGridGrouped_returns_400_validation_problem_in_strict_mode()
    {
        var client = await ServerAsync();
        var response = await client.GetAsync("/strict/groups?groupBy=group&nope_eq=1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Strict_endpoint_still_returns_200_for_a_valid_request()
    {
        var client = await ServerAsync();
        var response = await client.GetAsync("/strict?sort=-score&pageSize=5");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
