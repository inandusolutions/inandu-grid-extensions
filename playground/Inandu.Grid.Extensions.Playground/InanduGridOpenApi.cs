using Inandu.Grid.Extensions.AspNetCore;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Inandu.Grid.Extensions.Playground;

/// <summary>
/// Adds the query parameters InanduGrid recognizes to any operation whose endpoint carries the
/// <see cref="InanduGridEndpoint"/> metadata. <c>MapInanduGrid*</c> add that metadata themselves; on an
/// endpoint wired by hand, chain <c>.WithMetadata(new InanduGridEndpoint())</c>. Registered once via
/// <c>options.OperationFilter&lt;InanduGridSwaggerFilter&gt;()</c> in <c>AddSwaggerGen</c> — see
/// <c>docs/openapi-swagger.md</c>.
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
        foreach (var p in InanduGridOpenApi.Parameters(metadata))
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
    }
}
