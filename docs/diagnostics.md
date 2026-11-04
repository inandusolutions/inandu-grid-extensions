# Diagnostics: traces and metrics

Every grid query (`ToInanduGrid`, `ToInanduGridGrouped`, `ToInanduGridDistinct` and their `…Async`
EF Core versions, and therefore the `MapInanduGrid*` endpoints) is reported through a standard
`System.Diagnostics` `ActivitySource` and `Meter`. Nothing is recorded unless something listens.

## Wiring it up

With OpenTelemetry:

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(InanduGridDiagnostics.SourceName))
    .WithMetrics(m => m.AddMeter(InanduGridDiagnostics.MeterName));
```

Both names are `"Inandu.Grid.Extensions"`.

## What you get

| Signal | Name | Details |
| --- | --- | --- |
| Trace | `inandu.grid.query`, `inandu.grid.grouped`, `inandu.grid.distinct` | tags `inandu.grid.operation` and `inandu.grid.total`; status `Error` if the query throws |
| Counter | `inandu.grid.requests` | tags `inandu.grid.operation` and `inandu.grid.outcome` |
| Histogram (ms) | `inandu.grid.request.duration` | same tags |

`inandu.grid.outcome` is `ok`, `rejected` (an `InanduGridRequestException`: a forbidden field in strict
mode, or a limit exceeded with `InanduGridLimitMode.Reject`) or `error` (anything else, e.g. a database
failure). The exception is always rethrown.

A rising `rejected` rate on a public endpoint is worth alerting on: it is clients asking for fields or
sizes you do not allow.

## What is deliberately not recorded

Field names, filter values, search text and row data never appear in a tag, so enabling the source on
an endpoint that handles personal data does not leak any. If you need the raw query, log it yourself
where you have the context to redact it.

Time spent is the full time of the query call, including the database round trips; for the SQL itself
use EF Core's own instrumentation (`Microsoft.EntityFrameworkCore` source).
