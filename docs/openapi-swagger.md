# Documenting InanduGrid endpoints in Swagger / OpenAPI

A minimal-API endpoint that reads its request by hand off `HttpRequest.QueryString` — which is
what every `ToInanduGrid()`/`ToInanduGridAsync()` call site does — has **no parameters ASP.NET
Core's model binding can see**. Swashbuckle documents what model binding sees, so by default these
endpoints show up in Swagger UI with an empty parameter list, even though they accept a dozen
query params (`page`, `sort`, `q`, column filters, `aggregate`, …).

`MapInanduGrid`/`MapInanduGridGrouped` (from `Inandu.Grid.Extensions.AspNetCore`) have the same
problem — they're minimal-API endpoints under the hood too.

There's no NuGet package for this (it would need to guess which endpoints are "InanduGrid
endpoints" and how, which is exactly what would make it fragile). Instead, here's the ~50-line
pattern the [playground](../playground/Inandu.Grid.Extensions.Playground/InanduGridOpenApi.cs)
actually uses — copy it into your project and adjust the parameter list to whatever your endpoint
supports (skip `groupBy`/`groupKeys` for a plain `ToInanduGrid()` endpoint, skip `after` unless
`EnableKeyset` is on, etc.).

## 1. A metadata marker

```csharp
public sealed class InanduGridEndpoint
{
    public bool Grouped { get; init; }
    public bool Keyset { get; init; }
}
```

## 2. An `IOperationFilter` that reads it

```csharp
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

public sealed class InanduGridSwaggerFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<InanduGridEndpoint>().FirstOrDefault();
        if (metadata is null) return;

        operation.Parameters ??= new List<IOpenApiParameter>();
        void Add(string name, string description, JsonSchemaType type = JsonSchemaType.String) =>
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = name, In = ParameterLocation.Query, Required = false,
                Description = description, Schema = new OpenApiSchema { Type = type },
            });

        Add("page", "1-based page number. Default 1.", JsonSchemaType.Integer);
        Add("pageSize", "Rows per page, clamped to MaxPageSize. Default 25.", JsonSchemaType.Integer);
        Add("sort", "Comma-separated fields; a leading '-' sorts descending, e.g. sort=-createdOn,name.");
        Add("q", "Free-text search across the configured SearchableFields.");
        Add("filter", "A JSON advanced-filter tree (nested AND/OR conditions).");
        Add("{field}_{op}", "A column filter, e.g. price_gte=20. op is one of eq, neq, contains, startsWith, endsWith, gt, gte, lt, lte, in.");
        Add("aggregate", "Comma-separated function:field totals over the filtered set, e.g. sum:amount,count:*.");
        if (metadata.Grouped)
        {
            Add("groupBy", "Comma-separated group fields, outermost first, e.g. groupBy=region,category.");
            Add("groupKeys", "The already-expanded drill-down path, e.g. groupKeys=EMEA.");
        }
        if (metadata.Keyset)
            Add("after", "Keyset cursor from a previous response's nextCursor — seeks past that row instead of Skip.");
    }
}
```

`Microsoft.OpenApi` 2.x renamed/moved several types from the old `Microsoft.OpenApi.Models`
namespace — the snippet above targets `Swashbuckle.AspNetCore` 10.x / `Microsoft.OpenApi` 2.x, the
versions the playground pins. If you're on an older Swashbuckle, `Microsoft.OpenApi.Models` and a
plain `string` schema type (`"integer"`, `"string"`) are the pre-2.x equivalents.

## 3. Wire it up

```csharp
builder.Services.AddSwaggerGen(o => o.OperationFilter<InanduGridSwaggerFilter>());

app.MapGet("/api/products", (HttpRequest request) => /* ... */)
    .WithMetadata(new InanduGridEndpoint());

app.MapInanduGrid("/api/catalog", products, o => { o.EnableKeyset = true; })
    .WithMetadata(new InanduGridEndpoint { Keyset = true }); // MapInanduGrid returns a RouteHandlerBuilder, so this chains directly
```

That's it — every `.WithMetadata(new InanduGridEndpoint())` endpoint now lists `page`, `sort`, `q`,
etc. as real, documented, triable-from-Swagger-UI query parameters. An MVC controller action bound
via `[FromInanduGrid] InanduGridRequest request` doesn't need any of this — ASP.NET Core's binder
already exposes `InanduGridRequest`'s own properties to Swashbuckle the normal way.
