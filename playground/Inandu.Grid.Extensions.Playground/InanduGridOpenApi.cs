using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Inandu.Grid.Extensions.Playground;

/// <summary>
/// Tags a minimal-API endpoint as reading its request through InanduGrid's query-string contract
/// (<c>page</c>/<c>pageSize</c>/<c>sort</c>/<c>q</c>/column filters/…). ASP.NET Core's model binding
/// — and therefore Swashbuckle — has no way to see parameters an endpoint reads by hand off
/// <c>HttpRequest.QueryString</c>; without this metadata those endpoints show up in Swagger UI with
/// no query parameters documented at all, even though they accept a dozen of them.
/// </summary>
public sealed class InanduGridEndpoint
{
    /// <summary>Document <c>groupBy</c>/<c>groupKeys</c> too — only meaningful on a <c>ToInanduGridGrouped</c> endpoint.</summary>
    public bool Grouped { get; init; }
    /// <summary>Document <c>after</c> (keyset cursor) too — only meaningful when <c>EnableKeyset</c> is on.</summary>
    public bool Keyset { get; init; }
}

/// <summary>
/// Adds the query parameters InanduGrid recognizes to any operation whose endpoint carries
/// <see cref="InanduGridEndpoint"/> metadata (<c>.WithMetadata(new InanduGridEndpoint())</c>).
/// Registered once via <c>options.OperationFilter&lt;InanduGridSwaggerFilter&gt;()</c> in
/// <c>AddSwaggerGen</c> — see <c>docs/openapi-swagger.md</c> for the full write-up.
/// </summary>
public sealed class InanduGridSwaggerFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata
            .OfType<InanduGridEndpoint>()
            .FirstOrDefault();
        if (metadata is null) return;

        operation.Parameters ??= new List<IOpenApiParameter>();

        void Add(string name, string description, JsonSchemaType type = JsonSchemaType.String) =>
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = name,
                In = ParameterLocation.Query,
                Required = false,
                Description = description,
                Schema = new OpenApiSchema { Type = type },
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
        {
            Add("after", "Keyset cursor from a previous response's nextCursor — seeks past that row instead of Skip.");
        }
    }
}
