using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Inandu.Grid.Extensions.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Inandu.Grid.Extensions.AspNetCore.Tests;

/// <summary>A Map* endpoint left in the permissive default says so once, in the log, when it is mapped.</summary>
public class PermissiveWarningTests
{
    private sealed record Item(int Id, string Name);

    private sealed class ListLoggerProvider : ILoggerProvider
    {
        public List<string> Warnings { get; } = new();

        public ILogger CreateLogger(string categoryName) => new ListLogger(this);

        public void Dispose()
        {
        }

        private sealed class ListLogger : ILogger
        {
            private readonly ListLoggerProvider _owner;

            public ListLogger(ListLoggerProvider owner) => _owner = owner;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                {
                    _owner.Warnings.Add(formatter(state, exception));
                }
            }
        }
    }

    private static async Task<List<string>> MapAsync(Action<IEndpointRouteBuilder> map)
    {
        var logs = new ListLoggerProvider();
        var host = await new HostBuilder().ConfigureWebHost(web =>
        {
            web.UseTestServer();
            web.ConfigureLogging(l => l.AddProvider(logs));
            web.ConfigureServices(s => s.AddRouting());
            web.Configure(app =>
            {
                app.UseRouting();
                app.UseEndpoints(map);
            });
        }).StartAsync();
        host.Dispose();
        return logs.Warnings;
    }

    private static readonly List<Item> Data = Enumerable.Range(1, 3).Select(i => new Item(i, $"n{i}")).ToList();

    [Fact]
    public async Task An_unconfigured_endpoint_logs_the_warning()
    {
        var warnings = await MapAsync(e => e.MapInanduGrid("/items", Data));

        var warning = Assert.Single(warnings);
        Assert.Contains("/items", warning);
        Assert.Contains("AllowUnconfiguredFields", warning);
    }

    [Fact]
    public async Task A_deny_by_default_endpoint_logs_nothing()
    {
        var warnings = await MapAsync(e => e.MapInanduGrid("/items", Data, o =>
        {
            o.AllowUnconfiguredFields = false;
            o.Column("name");
        }));

        Assert.Empty(warnings);
    }

    [Fact]
    public async Task Each_permissive_endpoint_warns_once_under_its_own_pattern()
    {
        var warnings = await MapAsync(e =>
        {
            e.MapInanduGrid("/a", Data);
            e.MapInanduGridGrouped("/b", Data);
            e.MapInanduGridDistinct("/c/{field}", Data);
        });

        Assert.Equal(3, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("'/a'"));
        Assert.Contains(warnings, w => w.Contains("'/b'"));
        Assert.Contains(warnings, w => w.Contains("'/c/{field}'"));
    }
}
