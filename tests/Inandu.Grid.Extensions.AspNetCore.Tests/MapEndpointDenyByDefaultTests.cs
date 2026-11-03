using System.Collections.Generic;
using System.Linq;
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
/// The blind-extraction scenario over HTTP: with <c>AllowUnconfiguredFields = false</c>, probing an
/// undeclared field (<c>passwordHash_startsWith=…</c>) no longer changes <c>total</c>.
/// </summary>
public class MapEndpointDenyByDefaultTests
{
    private sealed record User(int Id, string Name, string PasswordHash);

    private static readonly List<User> Users = Enumerable.Range(1, 20)
        .Select(i => new User(i, $"User {i:D2}", i == 7 ? "a1b2c3" : $"z{i}"))
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
                    e.MapInanduGrid("/open", Users);
                    e.MapInanduGrid("/locked", Users, o =>
                    {
                        o.AllowUnconfiguredFields = false;
                        o.Column("name").Searchable();
                    });
                });
            });
            web.ConfigureServices(s => s.AddRouting());
        });

        var host = await builder.StartAsync();
        return host.GetTestClient();
    }

    private static async Task<int> TotalAsync(HttpClient client, string url)
        => (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("total").GetInt32();

    [Fact]
    public async Task Default_endpoint_leaks_through_total()
    {
        var client = await ServerAsync();

        Assert.Equal(1, await TotalAsync(client, "/open?passwordHash_startsWith=a"));
        Assert.Equal(1, await TotalAsync(client, "/open?q=a1b2c3")); // every string property is searched
    }

    [Fact]
    public async Task Locked_endpoint_does_not_leak_through_total()
    {
        var client = await ServerAsync();

        Assert.Equal(Users.Count, await TotalAsync(client, "/locked?passwordHash_startsWith=a"));
        Assert.Equal(Users.Count, await TotalAsync(client, "/locked?sort=passwordHash&pageSize=1"));
        Assert.Equal(0, await TotalAsync(client, "/locked?q=a1b2c3")); // only name is searched
    }

    [Fact]
    public async Task Locked_endpoint_still_filters_configured_columns()
    {
        var client = await ServerAsync();

        Assert.Equal(1, await TotalAsync(client, "/locked?name_eq=User 07"));
    }
}
