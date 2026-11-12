# Compatibility

What runs with what. Anything not listed here is untested.

## .NET packages

| Package | Targets | Notes |
| --- | --- | --- |
| `Inandu.Grid.Extensions` | `net8.0`, `net10.0`, `net11.0` | no dependencies on ASP.NET Core or EF Core |
| `Inandu.Grid.Extensions.AspNetCore` | `net8.0`, `net10.0`, `net11.0` | uses the shared framework (`Microsoft.AspNetCore.App`) |
| `Inandu.Grid.Extensions.EntityFrameworkCore` | `net8.0`, `net10.0`, `net11.0` | EF Core `8.0.11` on net8.0, `10.0.14` on net10.0, `11.0.0` on net11.0 |

The `net10.0` target is built with the .NET 10 SDK or later and `net11.0` with the .NET 11 SDK. Existing `net8.0` and `net10.0` consumers are unaffected. All three packages of a release share one
version number; install matching versions.

### Databases

The EF Core tests (`Inandu.Grid.Extensions.EntityFrameworkCore.Tests`) run in CI against **SQLite**
(in memory), **SQL Server 2022**, **PostgreSQL 17** and **MySQL 8.4**. SQLite and SQL Server run on net8.0, net10.0 and net11.0; PostgreSQL on net8.0 and net10.0 (Npgsql has no EF Core 11 release yet);
MySQL runs on net8.0 only, because the Pomelo provider has no EF Core 10 release yet. The same tests run on each
provider: paging, sorting, filters, advanced filter, grouping, distinct, aggregates, keyset (including null sort keys),
deny-by-default and projections. Other providers (Oracle, MariaDB) are not covered: test the endpoints you
expose, in particular string filters (`Contains`, `StartsWith`), case sensitivity and grouping.

Run them against your own server with `INANDU_TEST_PROVIDER` (`sqlserver`, `postgres` or `mysql`) and
`INANDU_TEST_CONNECTION` (a server-level connection string whose user can create databases). For MySQL you can leave
`INANDU_TEST_CONNECTION` out: the tests start a throw-away MySQL container with
[Testcontainers](https://dotnet.testcontainers.org/) (Docker must be running):

```
INANDU_TEST_PROVIDER=mysql dotnet test tests/Inandu.Grid.Extensions.EntityFrameworkCore.Tests -f net8.0
```

## Front ends

The request contract (`page`/`pageSize` or `offset`/`limit`, `sort`, `q`, `{field}_{op}`, `groupBy`, …)
is plain query-string HTTP, so any client works. The packages below speak it out of the box and are
tested against the shared contract fixtures in `tests/**/Contract`.

| Front end package | Framework | Server-side mode |
| --- | --- | --- |
| `@inandu-solutions/grid-angular` | Angular `^21.2.0 \|\| ^22.0.0` | `serverSide` |
| `@inandu-solutions/grid-react` | React `^18 \|\| ^19` | `useInanduGridServer` |
| `@inandu-solutions/grid-vue` | Vue `^3.4` | `useInanduGridServer` |
| `@inandu-solutions/grid-pro` (Angular), `-pro-react`, `-pro-vue` (commercial) | as the free packages | row model, data source and adapter helpers |

### Minimum `Inandu.Grid.Extensions` version per feature

| You use | Minimum |
| --- | --- |
| Any free grid with `ToInanduGrid()` | any release; **0.4.0 and earlier are vulnerable**, use `>= 0.4.1` |
| 400 `ValidationProblemDetails` from `MapInanduGrid*` | 0.4.1 |
| Deny-by-default (`AllowUnconfiguredFields = false`) | 0.5.0 |
| `grid-pro` server row model with grouping | 0.4.1 (0.5.0 recommended) |

## Versioning

Packages follow SemVer. From 0.5.0 on, public API compatibility is checked at pack time against the
previous release (`PackageValidationBaselineVersion`).
