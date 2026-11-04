using System.Collections.Generic;

namespace Inandu.Grid.Extensions.AspNetCore;

/// <summary>
/// Endpoint metadata that says "this endpoint reads its request through the InanduGrid query-string
/// contract". ASP.NET Core's model binding — and so Swagger / OpenAPI generators — cannot see
/// parameters an endpoint reads by hand off <c>HttpRequest.QueryString</c>; this marker, plus
/// <see cref="InanduGridOpenApi.Parameters"/>, lets a filter or transformer document them.
/// <para>
/// <c>MapInanduGrid</c>, <c>MapInanduGridGrouped</c> and <c>MapInanduGridDistinct</c> add it for you.
/// Add it yourself (<c>.WithMetadata(new InanduGridEndpoint())</c>) on endpoints you wire by hand.
/// </para>
/// </summary>
public sealed class InanduGridEndpoint
{
    /// <summary>Also document <c>groupBy</c> / <c>groupKeys</c> (a <c>ToInanduGridGrouped</c> endpoint).</summary>
    public bool Grouped { get; init; }

    /// <summary>Also document <c>after</c>, the keyset cursor (only meaningful with <c>EnableKeyset</c>).</summary>
    public bool Keyset { get; init; }

    /// <summary>A distinct-values endpoint (<c>ToInanduGridDistinct</c>): documents <c>field</c> and leaves out <c>aggregate</c>.</summary>
    public bool Distinct { get; init; }
}

/// <summary>One documented query-string parameter.</summary>
/// <param name="Name">Parameter name (the column-filter pattern is written <c>{field}_{op}</c>).</param>
/// <param name="Description">Human description.</param>
/// <param name="IsInteger"><c>true</c> for an integer parameter, <c>false</c> for a string.</param>
public sealed record InanduGridOpenApiParameter(string Name, string Description, bool IsInteger = false);

/// <summary>The query parameters InanduGrid recognises, for OpenAPI generators.</summary>
public static class InanduGridOpenApi
{
    /// <summary>The parameters an endpoint with the given metadata accepts, in display order.</summary>
    public static IReadOnlyList<InanduGridOpenApiParameter> Parameters(InanduGridEndpoint endpoint)
    {
        var list = new List<InanduGridOpenApiParameter>
        {
            new("page", "1-based page number. Default 1.", true),
            new("pageSize", "Rows per page, clamped to MaxPageSize. Default 25.", true),
            new("sort", "Comma-separated fields; a leading '-' sorts descending, e.g. sort=-createdOn,name."),
            new("q", "Free-text search across the configured SearchableFields."),
            new("filter", "A JSON advanced-filter tree (nested AND/OR conditions)."),
            new("{field}_{op}", "A column filter, e.g. price_gte=20. op is one of eq, neq, contains, startsWith, endsWith, gt, gte, lt, lte, in."),
        };

        if (endpoint.Distinct)
        {
            list.Add(new("field", "The column whose distinct values are listed (when not given in the route)."));
            return list;
        }

        list.Add(new("aggregate", "Comma-separated function:field totals over the filtered set, e.g. sum:amount,count:*."));

        if (endpoint.Grouped)
        {
            list.Add(new("groupBy", "Comma-separated group fields, outermost first, e.g. groupBy=region,category."));
            list.Add(new("groupKeys", "The already-expanded drill-down path, e.g. groupKeys=EMEA."));
        }

        if (endpoint.Keyset)
        {
            list.Add(new("after", "Keyset cursor from a previous response's nextCursor; seeks past that row instead of skipping."));
        }

        return list;
    }
}
