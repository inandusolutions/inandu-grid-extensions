using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Inandu.Grid.Extensions.AspNetCore;

/// <summary>
/// One-liner minimal-API endpoints — bind the request from the HTTP request, run
/// <c>ToInanduGrid</c> / <c>ToInanduGridGrouped</c> / <c>ToInanduGridDistinct</c> over an in-memory
/// source, and return the JSON. For EF Core / <c>async</c>, wire the endpoint yourself and call the
/// EntityFrameworkCore package's <c>*Async</c> methods.
/// </summary>
public static class MapInanduGridEndpoints
{
    /// <summary>GET endpoint that returns a page of <paramref name="source"/> as an <see cref="InanduGridResult{T}"/>.</summary>
    public static RouteHandlerBuilder MapInanduGrid<T>(
        this IEndpointRouteBuilder endpoints, string pattern, IEnumerable<T> source, Action<InanduGridOptions>? configure = null)
        => endpoints.MapInanduGrid(pattern, _ => source, configure);

    /// <summary>GET endpoint whose data comes from <paramref name="source"/> per request (e.g. a scoped <c>DbSet</c>).</summary>
    public static RouteHandlerBuilder MapInanduGrid<T>(
        this IEndpointRouteBuilder endpoints, string pattern, Func<HttpContext, IEnumerable<T>> source, Action<InanduGridOptions>? configure = null)
        => endpoints.MapGet(pattern, async (HttpContext ctx) =>
            {
                var request = await InanduGridBinding.FromHttpRequestAsync(ctx.Request, ctx.RequestAborted).ConfigureAwait(false);
                return Results.Ok(source(ctx).ToInanduGrid(InanduGridOptions.For(request, configure)));
            })
            .Produces<InanduGridResult<T>>();

    /// <summary>GET endpoint for server-side grouping (<c>?groupBy=…&amp;groupKeys=…</c>).</summary>
    public static RouteHandlerBuilder MapInanduGridGrouped<T>(
        this IEndpointRouteBuilder endpoints, string pattern, Func<HttpContext, IEnumerable<T>> source, Action<InanduGridOptions>? configure = null)
        => endpoints.MapGet(pattern, async (HttpContext ctx) =>
            {
                var request = await InanduGridBinding.FromHttpRequestAsync(ctx.Request, ctx.RequestAborted).ConfigureAwait(false);
                return Results.Ok(source(ctx).ToInanduGridGrouped(InanduGridOptions.For(request, configure)));
            })
            .Produces<InanduGridGroupedResult<T>>();

    /// <inheritdoc cref="MapInanduGridGrouped{T}(IEndpointRouteBuilder, string, Func{HttpContext, IEnumerable{T}}, Action{InanduGridOptions}?)"/>
    public static RouteHandlerBuilder MapInanduGridGrouped<T>(
        this IEndpointRouteBuilder endpoints, string pattern, IEnumerable<T> source, Action<InanduGridOptions>? configure = null)
        => endpoints.MapInanduGridGrouped(pattern, _ => source, configure);

    /// <summary>
    /// GET endpoint for a column's distinct values. The field comes from the <c>{field}</c> route
    /// value or the <c>field</c> query param, unless <paramref name="fieldSelector"/> is given.
    /// </summary>
    public static RouteHandlerBuilder MapInanduGridDistinct<T>(
        this IEndpointRouteBuilder endpoints, string pattern, Func<HttpContext, IEnumerable<T>> source,
        Func<HttpContext, string?>? fieldSelector = null, Action<InanduGridOptions>? configure = null)
        => endpoints.MapGet(pattern, async (HttpContext ctx) =>
            {
                var field = (fieldSelector?.Invoke(ctx)
                             ?? ctx.Request.RouteValues["field"]?.ToString()
                             ?? ctx.Request.Query["field"].FirstOrDefault())?.Trim();

                if (string.IsNullOrEmpty(field))
                {
                    return Results.BadRequest(new { error = "a 'field' route value or query parameter is required." });
                }

                var request = await InanduGridBinding.FromHttpRequestAsync(ctx.Request, ctx.RequestAborted).ConfigureAwait(false);
                return Results.Ok(source(ctx).ToInanduGridDistinct(field, InanduGridOptions.For(request, configure)));
            })
            .Produces<InanduGridDistinctResult>()
            .Produces(StatusCodes.Status400BadRequest);

    /// <inheritdoc cref="MapInanduGridDistinct{T}(IEndpointRouteBuilder, string, Func{HttpContext, IEnumerable{T}}, Func{HttpContext, string?}?, Action{InanduGridOptions}?)"/>
    public static RouteHandlerBuilder MapInanduGridDistinct<T>(
        this IEndpointRouteBuilder endpoints, string pattern, IEnumerable<T> source,
        Func<HttpContext, string?>? fieldSelector = null, Action<InanduGridOptions>? configure = null)
        => endpoints.MapInanduGridDistinct(pattern, _ => source, fieldSelector, configure);
}
