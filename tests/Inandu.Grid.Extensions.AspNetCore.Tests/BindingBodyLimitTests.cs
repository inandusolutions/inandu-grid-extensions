using System.IO;
using System.Text;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Xunit;

namespace Inandu.Grid.Extensions.AspNetCore.Tests;

/// <summary>A grid request is a handful of short strings: the JSON body is capped well below the host's default.</summary>
public class BindingBodyLimitTests
{
    private static HttpContext JsonPost(string body)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/json";
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Request.ContentLength = ctx.Request.Body.Length;
        return ctx;
    }

    [Fact]
    public async Task A_body_over_the_default_cap_is_refused_with_413()
    {
        var ctx = JsonPost("{\"after\":\"" + new string('A', (int)InanduGridBinding.DefaultMaxBodyBytes) + "\"}");

        var ex = await Assert.ThrowsAsync<BadHttpRequestException>(
            async () => await InanduGridBinding.FromHttpRequestAsync(ctx.Request, TestContext.Current.CancellationToken));

        Assert.Equal(StatusCodes.Status413PayloadTooLarge, ex.StatusCode);
    }

    [Fact]
    public async Task A_body_within_the_cap_still_binds()
    {
        var ctx = JsonPost("{\"page\":2,\"pageSize\":10}");

        var request = await InanduGridBinding.FromHttpRequestAsync(ctx.Request, TestContext.Current.CancellationToken);

        Assert.Equal(2, request.Page);
    }

    [Fact]
    public async Task The_cap_can_be_set_per_call()
    {
        var ctx = JsonPost("{\"query\":\"" + new string('a', 200) + "\"}");

        await Assert.ThrowsAsync<BadHttpRequestException>(
            async () => await InanduGridBinding.FromHttpRequestAsync(ctx.Request, 100, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Zero_leaves_the_host_limit_alone()
    {
        var ctx = JsonPost("{\"query\":\"" + new string('a', (int)InanduGridBinding.DefaultMaxBodyBytes) + "\"}");

        var request = await InanduGridBinding.FromHttpRequestAsync(ctx.Request, 0, TestContext.Current.CancellationToken);

        Assert.NotNull(request.Query);
    }

    [Fact]
    public async Task A_chunked_body_gets_the_server_limit_set_to_the_cap()
    {
        var ctx = JsonPost("{\"page\":1}");
        ctx.Request.ContentLength = null; // chunked: no Content-Length to look at
        var feature = new TestMaxBodyFeature();
        ctx.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);

        await InanduGridBinding.FromHttpRequestAsync(ctx.Request, 4096, TestContext.Current.CancellationToken);

        Assert.Equal(4096, feature.MaxRequestBodySize);
    }

    private sealed class TestMaxBodyFeature : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;

        public long? MaxRequestBodySize { get; set; } = 30_000_000;
    }
}
