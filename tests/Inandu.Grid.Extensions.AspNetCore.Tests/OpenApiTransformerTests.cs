#if NET10_0_OR_GREATER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi;
using Xunit;

namespace Inandu.Grid.Extensions.AspNetCore.Tests;

/// <summary>
/// The transformer below is the one documented in docs/openapi-swagger.md; this proves the snippet
/// compiles and produces the parameters in a real generated document.
/// </summary>
public class OpenApiTransformerTests
{
    private sealed record Item(int Id, string Name);

    private sealed class InanduGridOperationTransformer : IOpenApiOperationTransformer
    {
        public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
        {
            var endpoint = context.Description.ActionDescriptor.EndpointMetadata.OfType<InanduGridEndpoint>().FirstOrDefault();
            if (endpoint is null)
            {
                return Task.CompletedTask;
            }

            operation.Parameters ??= new List<IOpenApiParameter>();
            foreach (var p in InanduGridOpenApi.Parameters(endpoint))
            {
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = p.Name,
                    In = ParameterLocation.Query,
                    Required = false,
                    Description = p.Description,
                    Schema = new OpenApiSchema { Type = p.IsInteger ? JsonSchemaType.Integer : JsonSchemaType.String },
                });
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Mapped_endpoints_list_the_grid_query_parameters_in_the_generated_document()
    {
        var host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureServices(s =>
            {
                s.AddRouting();
                s.AddOpenApi(o => o.AddOperationTransformer<InanduGridOperationTransformer>());
            });
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(e =>
                {
                    e.MapOpenApi();
                    e.MapInanduGrid("/items", new List<Item>());
                    e.MapInanduGridGrouped("/items/groups", new List<Item>());
                });
            });
        }).StartAsync();

        var json = await host.GetTestClient().GetStringAsync("/openapi/v1.json");
        using var doc = JsonDocument.Parse(json);
        string[] Params(string path) => doc.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get")
            .GetProperty("parameters").EnumerateArray().Select(p => p.GetProperty("name").GetString()!).ToArray();

        Assert.Contains("sort", Params("/items"));
        Assert.Contains("{field}_{op}", Params("/items"));
        Assert.DoesNotContain("groupBy", Params("/items"));
        Assert.Contains("groupBy", Params("/items/groups"));
    }
}
#endif
