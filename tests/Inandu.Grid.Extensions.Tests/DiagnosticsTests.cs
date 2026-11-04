using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Threading;
using Xunit;

namespace Inandu.Grid.Extensions.Tests;

/// <summary>
/// The listeners are process-wide and other tests run in parallel, so each test flags its own
/// execution context (<see cref="Scope"/>) and the listeners ignore everything started outside it.
/// </summary>
public class DiagnosticsTests
{
    private sealed class Scope : IDisposable
    {
        private static readonly AsyncLocal<bool> Active = new();
        public List<Activity> Activities { get; } = new();
        public List<(string Instrument, double Value, Dictionary<string, object?> Tags)> Measurements { get; } = new();
        private readonly ActivityListener _activityListener;
        private readonly MeterListener _meterListener = new();
        private readonly object _gate = new();

        public Scope()
        {
            Active.Value = true;
            _activityListener = new ActivityListener
            {
                ShouldListenTo = s => s.Name == InanduGridDiagnostics.SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = a => { if (Active.Value) { lock (_gate) { Activities.Add(a); } } },
            };
            ActivitySource.AddActivityListener(_activityListener);

            _meterListener.InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == InanduGridDiagnostics.MeterName) { l.EnableMeasurementEvents(inst); }
            };
            _meterListener.SetMeasurementEventCallback<long>((i, v, tags, _) => Record(i.Name, v, tags));
            _meterListener.SetMeasurementEventCallback<double>((i, v, tags, _) => Record(i.Name, v, tags));
            _meterListener.Start();
        }

        private void Record(string name, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            if (!Active.Value) { return; }
            var dict = tags.ToArray().ToDictionary(t => t.Key, t => t.Value);
            lock (_gate) { Measurements.Add((name, value, dict)); }
        }

        public void Dispose()
        {
            Active.Value = false;
            _activityListener.Dispose();
            _meterListener.Dispose();
        }
    }

    private static InanduGridOptions Options(Action<InanduGridRequest>? shape = null, Action<InanduGridOptions>? configure = null)
    {
        var request = new InanduGridRequest { PageSize = 10 };
        shape?.Invoke(request);
        return InanduGridOptions.For(request, o => configure?.Invoke(o));
    }

    [Fact]
    public void A_query_emits_one_activity_with_the_total_and_counts_as_ok()
    {
        using var scope = new Scope();

        var result = Sample.Products().ToInanduGrid(Options());

        var activity = Assert.Single(scope.Activities);
        Assert.Equal("inandu.grid.query", activity.OperationName);
        Assert.Equal("query", activity.GetTagItem("inandu.grid.operation"));
        Assert.Equal(result.Total, activity.GetTagItem("inandu.grid.total"));

        var count = Assert.Single(scope.Measurements, m => m.Instrument == "inandu.grid.requests");
        Assert.Equal(1, count.Value);
        Assert.Equal("ok", count.Tags["inandu.grid.outcome"]);
        Assert.Equal("query", count.Tags["inandu.grid.operation"]);
        Assert.Single(scope.Measurements, m => m.Instrument == "inandu.grid.request.duration");
    }

    [Fact]
    public void Grouped_and_distinct_queries_have_their_own_operation()
    {
        using var scope = new Scope();

        Sample.Products().ToInanduGridGrouped(Options(r => r.GroupBy.Add("status")));
        Sample.Products().ToInanduGridDistinct("status", Options());

        Assert.Equal(new[] { "inandu.grid.grouped", "inandu.grid.distinct" }, scope.Activities.Select(a => a.OperationName).ToArray());
    }

    [Fact]
    public void A_rejected_request_is_recorded_as_rejected_and_the_exception_still_propagates()
    {
        using var scope = new Scope();
        var options = Options(
            r =>
            {
                r.Conditions.Add(new FilterCondition("price", FilterOperator.GreaterThan, "1"));
                r.Conditions.Add(new FilterCondition("price", FilterOperator.LessThan, "100"));
            },
            o =>
            {
                o.Limits.MaxConditions = 1;
                o.OnLimitExceeded = InanduGridLimitMode.Reject;
            });

        Assert.Throws<InanduGridRequestException>(() => Sample.Products().ToInanduGrid(options));

        var activity = Assert.Single(scope.Activities);
        Assert.Equal(ActivityStatusCode.Error, activity.Status);
        var count = Assert.Single(scope.Measurements, m => m.Instrument == "inandu.grid.requests");
        Assert.Equal("rejected", count.Tags["inandu.grid.outcome"]);
    }

    [Fact]
    public void Nothing_about_the_request_is_recorded()
    {
        using var scope = new Scope();

        Sample.Products().ToInanduGrid(Options(r =>
            r.Conditions.Add(new FilterCondition("name", FilterOperator.Contains, "secret-needle"))));

        var activity = Assert.Single(scope.Activities);
        Assert.Equal(
            new[] { "inandu.grid.operation", "inandu.grid.total" },
            activity.TagObjects.Select(t => t.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(scope.Measurements, m => m.Tags.Values.Any(v => v is string s && s.Contains("secret-needle")));
    }
}
