using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
    {
        var builder = endpoints.MapGet(    pattern, async (HttpContext ctx) =>
                {
                    var request = await InanduGridBinding.FromHttpRequestAsync(ctx.Request, ctx.RequestAborted).ConfigureAwait(false);
                    return Run(() => source(ctx).ToInanduGrid(InanduGridOptions.For(request, configure)));
                })
                .Produces<InanduGridResult<T>>()
                .ProducesValidationProblem()
                .WithMetadata(new InanduGridEndpoint { Keyset = KeysetEnabled(configure) });

        WarnIfPermissive(endpoints, pattern, configure);
        return builder;
    }

    /// <summary>GET endpoint for server-side grouping (<c>?groupBy=…&amp;groupKeys=…</c>).</summary>
    public static RouteHandlerBuilder MapInanduGridGrouped<T>(
        this IEndpointRouteBuilder endpoints, string pattern, Func<HttpContext, IEnumerable<T>> source, Action<InanduGridOptions>? configure = null)
    {
        var builder = endpoints.MapGet(    pattern, async (HttpContext ctx) =>
                {
                    var request = await InanduGridBinding.FromHttpRequestAsync(ctx.Request, ctx.RequestAborted).ConfigureAwait(false);
                    return Run(() => source(ctx).ToInanduGridGrouped(InanduGridOptions.For(request, configure)));
                })
                .Produces<InanduGridGroupedResult<T>>()
                .ProducesValidationProblem()
                .WithMetadata(new InanduGridEndpoint { Grouped = true });

        WarnIfPermissive(endpoints, pattern, configure);
        return builder;
    }

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
    {
        var builder = endpoints.MapGet(    pattern, async (HttpContext ctx) =>
                {
                    var field = (fieldSelector?.Invoke(ctx)
                                 ?? ctx.Request.RouteValues["field"]?.ToString()
                                 ?? ctx.Request.Query["field"].FirstOrDefault())?.Trim();

                    if (string.IsNullOrEmpty(field))
                    {
                        return Results.ValidationProblem(new Dictionary<string, string[]>
                        {
                            ["field"] = new[] { "a 'field' route value or query parameter is required." },
                        });
                    }

                    var request = await InanduGridBinding.FromHttpRequestAsync(ctx.Request, ctx.RequestAborted).ConfigureAwait(false);
                    return Run(() => source(ctx).ToInanduGridDistinct(field, InanduGridOptions.For(request, configure)));
                })
                .Produces<InanduGridDistinctResult>()
                .ProducesValidationProblem()
                .WithMetadata(new InanduGridEndpoint { Distinct = true });

        WarnIfPermissive(endpoints, pattern, configure);
        return builder;
    }

    /// <inheritdoc cref="MapInanduGridDistinct{T}(IEndpointRouteBuilder, string, Func{HttpContext, IEnumerable{T}}, Func{HttpContext, string?}?, Action{InanduGridOptions}?)"/>
    public static RouteHandlerBuilder MapInanduGridDistinct<T>(
        this IEndpointRouteBuilder endpoints, string pattern, IEnumerable<T> source,
        Func<HttpContext, string?>? fieldSelector = null, Action<InanduGridOptions>? configure = null)
        => endpoints.MapInanduGridDistinct(pattern, _ => source, fieldSelector, configure);

    /// <summary>
    /// Logs a warning (once per mapped endpoint) when the endpoint is left in the permissive default,
    /// <see cref="InanduGridOptions.AllowUnconfiguredFields"/> = <c>true</c>: any public property,
    /// including navigation paths such as <c>customer.email</c>, can then be filtered, sorted,
    /// grouped, aggregated and listed as distinct values by whoever calls the endpoint.
    /// </summary>
    private static void WarnIfPermissive(IEndpointRouteBuilder endpoints, string pattern, Action<InanduGridOptions>? configure)
    {
        var logger = endpoints.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger("Inandu.Grid.Extensions");
        if (logger is null || !logger.IsEnabled(LogLevel.Warning))
        {
            return;
        }

        var probe = new InanduGridOptions();
        try
        {
            configure?.Invoke(probe);
        }
        catch (Exception)
        {
            // A configure delegate that needs request state can't be probed: say nothing rather than guess.
            return;
        }

        if (probe.AllowUnconfiguredFields)
        {
            logger.LogWarning(
                "Inandu grid endpoint '{Pattern}' accepts any public property of the source type (AllowUnconfiguredFields = true). " +
                "For an endpoint that takes untrusted input, set AllowUnconfiguredFields = false and declare the columns clients may use with o.Column(...).",
                pattern);
        }
    }

    /// <summary>Whether <paramref name="configure"/> turns keyset paging on (probed once, at mapping time).</summary>
    private static bool KeysetEnabled(Action<InanduGridOptions>? configure)
    {
        if (configure is null)
        {
            return false;
        }

        try
        {
            var probe = new InanduGridOptions();
            configure(probe);
            return probe.EnableKeyset;
        }
        catch (Exception)
        {
            // A configure delegate that needs request state can't be probed; just don't advertise `after`.
            return false;
        }
    }

    /// <summary>
    /// Runs the query and returns <c>200</c>, or — when the request is rejected (an unknown /
    /// forbidden field with <see cref="InanduGridOptions.ThrowOnUnknownField"/>, or a
    /// <see cref="InanduGridLimits"/> cap with <see cref="InanduGridLimitMode.Reject"/>) — a
    /// <c>400</c> <c>ValidationProblemDetails</c> built from <see cref="InanduGridRequestException.Errors"/>
    /// instead of an unhandled exception (HTTP 500).
    /// </summary>
    private static IResult Run<TResult>(Func<TResult> query)
    {
        try
        {
            return Results.Ok(query());
        }
        catch (InanduGridRequestException ex)
        {
            return ToValidationProblem(ex);
        }
    }

    /// <summary>The <c>400</c> response for a rejected request.</summary>
    internal static IResult ToValidationProblem(InanduGridRequestException ex)
    {
        var errors = ex.Errors.Count > 0
            ? new Dictionary<string, string[]>(ex.Errors)
            : new Dictionary<string, string[]> { ["request"] = new[] { ex.Message } };
        return Results.ValidationProblem(errors, detail: ex.Message);
    }
}
