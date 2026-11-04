using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;

namespace Inandu.Grid.Extensions;

/// <summary>
/// OpenTelemetry-friendly diagnostics for every grid query. Nothing is recorded unless something
/// listens, so there is no cost to leaving it on.
/// <para>
/// Subscribe by name: <c>.AddSource(InanduGridDiagnostics.SourceName)</c> for traces and
/// <c>.AddMeter(InanduGridDiagnostics.MeterName)</c> for metrics.
/// </para>
/// <list type="bullet">
/// <item><description><b>Trace</b>: one activity per query, named <c>inandu.grid.query</c>, <c>inandu.grid.grouped</c> or <c>inandu.grid.distinct</c>, with the tags <c>inandu.grid.operation</c> and <c>inandu.grid.total</c>; the status is <c>Error</c> when the query throws.</description></item>
/// <item><description><b>Metric</b> <c>inandu.grid.requests</c> (counter): tags <c>inandu.grid.operation</c> and <c>inandu.grid.outcome</c> (<c>ok</c>, <c>rejected</c> for an <see cref="InanduGridRequestException"/>, <c>error</c>).</description></item>
/// <item><description><b>Metric</b> <c>inandu.grid.request.duration</c> (histogram, ms): same tags.</description></item>
/// </list>
/// Field names, filter values and row data are never recorded.
/// </summary>
public static class InanduGridDiagnostics
{
    /// <summary>Name of the <see cref="ActivitySource"/> (traces).</summary>
    public const string SourceName = "Inandu.Grid.Extensions";

    /// <summary>Name of the <see cref="Meter"/> (metrics).</summary>
    public const string MeterName = "Inandu.Grid.Extensions";

    internal const string Query = "query";
    internal const string Grouped = "grouped";
    internal const string Distinct = "distinct";

    private static readonly string Version = typeof(InanduGridDiagnostics).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    private static readonly ActivitySource Source = new(SourceName, Version);
    private static readonly Meter GridMeter = new(MeterName, Version);

    private static readonly Counter<long> Requests = GridMeter.CreateCounter<long>(
        "inandu.grid.requests", unit: "{request}", description: "Grid queries executed.");

    private static readonly Histogram<double> Duration = GridMeter.CreateHistogram<double>(
        "inandu.grid.request.duration", unit: "ms", description: "Time spent executing a grid query.");

    internal static TResult Run<TResult>(string operation, Func<TResult> run, Func<TResult, int> total)
    {
        var start = Stopwatch.GetTimestamp();
        using var activity = Source.StartActivity("inandu.grid." + operation);
        activity?.SetTag("inandu.grid.operation", operation);
        try
        {
            var result = run();
            Complete(operation, activity, start, "ok", total(result));
            return result;
        }
        catch (Exception ex)
        {
            Fail(operation, activity, start, ex);
            throw;
        }
    }

    internal static async Task<TResult> RunAsync<TResult>(string operation, Func<Task<TResult>> run, Func<TResult, int> total)
    {
        var start = Stopwatch.GetTimestamp();
        using var activity = Source.StartActivity("inandu.grid." + operation);
        activity?.SetTag("inandu.grid.operation", operation);
        try
        {
            var result = await run().ConfigureAwait(false);
            Complete(operation, activity, start, "ok", total(result));
            return result;
        }
        catch (Exception ex)
        {
            Fail(operation, activity, start, ex);
            throw;
        }
    }

    private static void Complete(string operation, Activity? activity, long start, string outcome, int total)
    {
        activity?.SetTag("inandu.grid.total", total);
        Record(operation, outcome, start);
    }

    private static void Fail(string operation, Activity? activity, long start, Exception ex)
    {
        var outcome = ex is InanduGridRequestException ? "rejected" : "error";
        activity?.SetStatus(ActivityStatusCode.Error, ex.GetType().Name);
        Record(operation, outcome, start);
    }

    private static void Record(string operation, string outcome, long start)
    {
        var tags = new TagList
        {
            { "inandu.grid.operation", operation },
            { "inandu.grid.outcome", outcome },
        };
        Requests.Add(1, tags);
        Duration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, tags);
    }
}
