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

/// <summary><c>groupKeysEncoding=repeat</c> through the real HTTP binder (URL decoding included).</summary>
public class GroupKeysEncodingHttpTests
{
    private sealed record Person(int Id, string Name, string Region);

    private static readonly List<Person> People = new()
    {
        new(1, "Smith, John", "EMEA"),
        new(2, "Smith, John", "APAC"),
        new(3, "Smith", "EMEA"),
        new(4, "", "EMEA"),
        new(5, " padded ", "EMEA"),
    };

    private static async Task<HttpClient> ServerAsync()
    {
        var host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(s => s.AddRouting());
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e => e.MapInanduGridGrouped("/people/groups", People));
            });
        }).StartAsync();
        return host.GetTestClient();
    }

    private static string[] Keys(JsonElement doc) =>
        doc.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("key").GetString()!).OrderBy(k => k).ToArray();

    [Fact]
    public async Task A_percent_encoded_comma_stays_inside_one_key()
    {
        var client = await ServerAsync();
        var doc = await client.GetFromJsonAsync<JsonElement>(
            "/people/groups?groupBy=name,region&groupKeys=Smith%2C%20John&groupKeysEncoding=repeat", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "APAC", "EMEA" }, Keys(doc));
    }

    [Fact]
    public async Task Without_the_marker_the_same_request_splits_the_key_as_before()
    {
        var client = await ServerAsync();
        // "Smith, John" -> ["Smith", "John"]: level 2 of name,region is the leaf; nobody is named "John", so no rows.
        var doc = await client.GetFromJsonAsync<JsonElement>(
            "/people/groups?groupBy=name,region&groupKeys=Smith%2C%20John", TestContext.Current.CancellationToken);

        Assert.True(doc.GetProperty("isLeaf").GetBoolean());
        Assert.Equal(0, doc.GetProperty("rows").GetArrayLength());
    }

    [Fact]
    public async Task A_plus_in_the_query_string_is_a_space_and_an_empty_value_is_the_empty_group()
    {
        var client = await ServerAsync();

        var padded = await client.GetFromJsonAsync<JsonElement>(
            "/people/groups?groupBy=name,region&groupKeys=+padded+&groupKeysEncoding=repeat", TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "EMEA" }, Keys(padded));

        var empty = await client.GetFromJsonAsync<JsonElement>(
            "/people/groups?groupBy=name,region&groupKeys=&groupKeysEncoding=repeat", TestContext.Current.CancellationToken);
        Assert.Equal(new[] { "EMEA" }, Keys(empty));
    }

    [Fact]
    public async Task Two_keys_reach_the_leaf_rows()
    {
        var client = await ServerAsync();
        var doc = await client.GetFromJsonAsync<JsonElement>(
            "/people/groups?groupBy=name,region&groupKeys=Smith%2C%20John&groupKeys=APAC&groupKeysEncoding=repeat", TestContext.Current.CancellationToken);

        Assert.True(doc.GetProperty("isLeaf").GetBoolean());
        var ids = doc.GetProperty("rows").EnumerateArray().Select(r => r.GetProperty("id").GetInt32()).ToArray();
        Assert.Equal(new[] { 2 }, ids);
    }

    [Fact]
    public async Task The_marker_is_not_taken_for_a_column_filter()
    {
        var client = await ServerAsync();
        var doc = await client.GetFromJsonAsync<JsonElement>(
            "/people/groups?groupBy=region&groupKeysEncoding=repeat", TestContext.Current.CancellationToken);

        Assert.Equal(new[] { "APAC", "EMEA" }, Keys(doc));
    }
}
