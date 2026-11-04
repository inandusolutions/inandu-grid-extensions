# Documenting InanduGrid endpoints in Swagger / OpenAPI

A minimal-API endpoint that reads its request by hand off `HttpRequest.QueryString` — which is
what every `ToInanduGrid()`/`ToInanduGridAsync()` call site does — has **no parameters ASP.NET
Core's model binding can see**. Swashbuckle documents what model binding sees, so by default these
endpoints show up in Swagger UI with an empty parameter list, even though they accept a dozen
query params (`page`, `sort`, `q`, column filters, `aggregate`, …).

`MapInanduGrid`, `MapInanduGridGrouped` and `MapInanduGridDistinct` (from `Inandu.Grid.Extensions.AspNetCore`)
have the same problem, so, from the release after 0.5.0, they **tag themselves** with an `InanduGridEndpoint` metadata marker, and the
package exposes the parameter list as data (`InanduGridOpenApi.Parameters(...)`). You add one small
adapter for the generator you use — it is deliberately not a package, so it does not drag Swashbuckle
or `Microsoft.AspNetCore.OpenApi` into your dependency graph.

```csharp
public sealed class InanduGridEndpoint            // Inandu.Grid.Extensions.AspNetCore
{
    public bool Grouped { get; init; }            // groupBy / groupKeys
    public bool Keyset  { get; init; }            // after (detected from EnableKeyset for MapInanduGrid)
    public bool Distinct { get; init; }           // field, no aggregate
}
```

An endpoint you wire by hand (`app.MapGet(..., (HttpRequest r) => r.QueryString ...)`) is not tagged
automatically: add `.WithMetadata(new InanduGridEndpoint())` to it.

## Microsoft.AspNetCore.OpenApi (.NET 9 / 10)

```csharp
using Inandu.Grid.Extensions.AspNetCore;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

public sealed class InanduGridOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var endpoint = context.Description.ActionDescriptor.EndpointMetadata.OfType<InanduGridEndpoint>().FirstOrDefault();
        if (endpoint is null) return Task.CompletedTask;

        operation.Parameters ??= new List<IOpenApiParameter>();
        foreach (var p in InanduGridOpenApi.Parameters(endpoint))
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = p.Name, In = ParameterLocation.Query, Required = false, Description = p.Description,
                Schema = new OpenApiSchema { Type = p.IsInteger ? JsonSchemaType.Integer : JsonSchemaType.String },
            });
        return Task.CompletedTask;
    }
}

builder.Services.AddOpenApi(o => o.AddOperationTransformer<InanduGridOperationTransformer>());
```

This exact transformer is compiled and run against a generated document in the test suite
(`OpenApiTransformerTests`, net10.0).

## Swashbuckle

```csharp
using Inandu.Grid.Extensions.AspNetCore;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

public sealed class InanduGridSwaggerFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var endpoint = context.ApiDescription.ActionDescriptor.EndpointMetadata.OfType<InanduGridEndpoint>().FirstOrDefault();
        if (endpoint is null) return;

        operation.Parameters ??= new List<IOpenApiParameter>();
        foreach (var p in InanduGridOpenApi.Parameters(endpoint))
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = p.Name, In = ParameterLocation.Query, Required = false, Description = p.Description,
                Schema = new OpenApiSchema { Type = p.IsInteger ? JsonSchemaType.Integer : JsonSchemaType.String },
            });
    }
}

builder.Services.AddSwaggerGen(o => o.OperationFilter<InanduGridSwaggerFilter>());
```

`Microsoft.OpenApi` 2.x renamed/moved several types from the old `Microsoft.OpenApi.Models`
namespace — the snippets target `Swashbuckle.AspNetCore` 10.x / `Microsoft.OpenApi` 2.x (and
`Microsoft.AspNetCore.OpenApi` 10.x), the versions the playground and the tests pin. On an older
Swashbuckle, `Microsoft.OpenApi.Models` and a plain `string` schema type (`"integer"`,
`"string"`) are the pre-2.x equivalents. The [playground](../playground/Inandu.Grid.Extensions.Playground/InanduGridOpenApi.cs)
uses the Swashbuckle version.

An MVC controller action bound via `[FromInanduGrid] InanduGridRequest request` doesn't need any of
this: ASP.NET Core's binder already exposes `InanduGridRequest`'s own properties to the generator
the normal way.
