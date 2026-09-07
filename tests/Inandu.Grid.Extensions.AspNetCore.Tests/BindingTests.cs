using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Inandu.Grid.Extensions.AspNetCore.Tests;

public class BindingTests
{
    private static HttpContext ContextWithQuery(string queryString)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "GET";
        ctx.Request.QueryString = new QueryString(queryString.StartsWith('?') ? queryString : "?" + queryString);
        return ctx;
    }

    [Fact]
    public void FromHttpRequest_parses_the_query_string()
    {
        var ctx = ContextWithQuery("?page=3&pageSize=15&sort=-createdOn,name&price_gte=20&q=abc&groupBy=region");
        var request = InanduGridBinding.FromHttpRequest(ctx.Request);

        Assert.Equal(3, request.Page);
        Assert.Equal(15, request.PageSize);
        Assert.Equal(new[] { "createdOn", "name" }, request.Sort.Select(s => s.Field));
        Assert.Equal(SortDirection.Descending, request.Sort[0].Direction);
        Assert.Equal("abc", request.Query);
        Assert.Contains(request.Conditions, c => c.Field == "price" && c.Operator == FilterOperator.GreaterThanOrEqual);
        Assert.Equal(new[] { "region" }, request.GroupBy);
    }

    [Fact]
    public void FromQuery_handles_repeated_keys()
    {
        var ctx = ContextWithQuery("?sort=name&sort=-price");
        var request = InanduGridBinding.FromQuery(ctx.Request.Query);

        Assert.Equal(new[] { "name", "price" }, request.Sort.Select(s => s.Field));
        Assert.True(request.Sort[1].IsDescending);
    }

    [Fact]
    public async Task FromHttpRequestAsync_reads_a_json_post_body()
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Method = "POST";
        ctx.Request.ContentType = "application/json";
        var body = """{"page":4,"pageSize":20,"query":"widget","sort":[{"field":"price","direction":"desc"}]}""";
        ctx.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        ctx.Request.ContentLength = ctx.Request.Body.Length;

        var request = await InanduGridBinding.FromHttpRequestAsync(ctx.Request);

        Assert.Equal(4, request.Page);
        Assert.Equal(20, request.PageSize);
        Assert.Equal("widget", request.Query);
        Assert.Single(request.Sort);
        Assert.Equal("price", request.Sort[0].Field);
        Assert.True(request.Sort[0].IsDescending);
    }

    [Fact]
    public async Task FromHttpRequestAsync_falls_back_to_query_when_body_is_not_json()
    {
        var ctx = ContextWithQuery("?page=7");
        ctx.Request.Method = "GET";
        var request = await InanduGridBinding.FromHttpRequestAsync(ctx.Request);
        Assert.Equal(7, request.Page);
    }

    [Fact]
    public async Task ModelBinder_binds_an_InanduGridRequest_parameter()
    {
        var ctx = ContextWithQuery("?page=2&sort=name&status_eq=Active");
        var binder = new InanduGridModelBinder();

        var metadataProvider = new EmptyModelMetadataProvider();
        var metadata = metadataProvider.GetMetadataForType(typeof(InanduGridRequest));
        var bindingContext = new DefaultModelBindingContext
        {
            ModelMetadata = metadata,
            ModelName = "request",
            ModelState = new ModelStateDictionary(),
            ActionContext = new ActionContext { HttpContext = ctx },
            ValueProvider = new QueryStringValueProvider(BindingSource.Query, ctx.Request.Query, System.Globalization.CultureInfo.InvariantCulture),
        };

        await binder.BindModelAsync(bindingContext);

        Assert.True(bindingContext.Result.IsModelSet);
        var request = Assert.IsType<InanduGridRequest>(bindingContext.Result.Model);
        Assert.Equal(2, request.Page);
        Assert.Single(request.Sort);
        Assert.Contains(request.Conditions, c => c.Field == "status" && c.Operator == FilterOperator.Equal);
    }

    [Fact]
    public void FromInanduGridAttribute_points_at_the_binder()
    {
        var attr = new FromInanduGridAttribute();
        Assert.Equal(typeof(InanduGridModelBinder), attr.BinderType);
    }

    [Fact]
    public void ModelBinderProvider_only_matches_InanduGridRequest()
    {
        var provider = new InanduGridModelBinderProvider();
        var metadataProvider = new EmptyModelMetadataProvider();

        var match = provider.GetBinder(new TestProviderContext(metadataProvider.GetMetadataForType(typeof(InanduGridRequest))));
        var miss = provider.GetBinder(new TestProviderContext(metadataProvider.GetMetadataForType(typeof(string))));

        Assert.NotNull(match);
        Assert.Null(miss);
    }

    private sealed class TestProviderContext : ModelBinderProviderContext
    {
        public TestProviderContext(ModelMetadata metadata) => Metadata = metadata;

        public override BindingInfo BindingInfo { get; } = new();

        public override ModelMetadata Metadata { get; }

        public override IModelMetadataProvider MetadataProvider { get; } = new EmptyModelMetadataProvider();

        public override IModelBinder CreateBinder(ModelMetadata metadata) => throw new NotSupportedException();
    }
}
