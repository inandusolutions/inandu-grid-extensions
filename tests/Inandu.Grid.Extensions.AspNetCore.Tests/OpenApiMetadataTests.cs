using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Inandu.Grid.Extensions.AspNetCore.Tests;

public class OpenApiMetadataTests
{
    private sealed record Item(int Id, string Name);

    private static readonly List<Item> Data = new() { new(1, "a"), new(2, "b") };

    private static async Task<IReadOnlyDictionary<string, InanduGridEndpoint?>> MetadataByRouteAsync()
    {
        var host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(s => s.AddRouting());
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e =>
                {
                    e.MapInanduGrid("/plain", Data);
                    e.MapInanduGrid("/keyset", Data, o => o.EnableKeyset = true);
                    e.MapInanduGrid("/throws", Data, _ => throw new InvalidOperationException("needs request state"));
                    e.MapInanduGridGrouped("/groups", Data);
                    e.MapInanduGridDistinct("/distinct/{field}", Data);
                });
            });
        }).StartAsync();

        return host.Services.GetServices<EndpointDataSource>()
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .ToDictionary(e => e.RoutePattern.RawText!, e => e.Metadata.GetMetadata<InanduGridEndpoint>());
    }

    [Fact]
    public async Task Map_helpers_tag_their_endpoints_so_openapi_can_describe_them()
    {
        var meta = await MetadataByRouteAsync();

        Assert.False(meta["/plain"]!.Grouped);
        Assert.False(meta["/plain"]!.Keyset);
        Assert.True(meta["/keyset"]!.Keyset);
        Assert.True(meta["/groups"]!.Grouped);
        Assert.True(meta["/distinct/{field}"]!.Distinct);
    }

    [Fact]
    public async Task A_configure_delegate_that_cannot_run_at_mapping_time_does_not_break_mapping()
    {
        var meta = await MetadataByRouteAsync();

        Assert.False(meta["/throws"]!.Keyset);
    }

    [Fact]
    public void Parameters_follow_the_endpoint_kind()
    {
        static string[] Names(InanduGridEndpoint e) => InanduGridOpenApi.Parameters(e).Select(p => p.Name).ToArray();

        var plain = Names(new InanduGridEndpoint());
        Assert.Equal(new[] { "page", "pageSize", "sort", "q", "filter", "{field}_{op}", "aggregate" }, plain);
        Assert.Contains("groupBy", Names(new InanduGridEndpoint { Grouped = true }));
        Assert.Contains("groupKeys", Names(new InanduGridEndpoint { Grouped = true }));
        Assert.Contains("after", Names(new InanduGridEndpoint { Keyset = true }));
        Assert.DoesNotContain("after", plain);

        var distinct = Names(new InanduGridEndpoint { Distinct = true });
        Assert.Contains("field", distinct);
        Assert.DoesNotContain("aggregate", distinct);
        Assert.True(InanduGridOpenApi.Parameters(new InanduGridEndpoint()).Single(p => p.Name == "page").IsInteger);
    }
}
