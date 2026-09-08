# Contributing to Inandu.Grid.Extensions

Thanks for taking the time to contribute.

## Ground rules

- For anything non-trivial, open an issue first so we can agree on the approach
  before you write code.
- Security issues: **do not** open a public issue — see
  [`.github/SECURITY.md`](.github/SECURITY.md).
- By contributing you agree your changes are licensed under the repo's
  [MIT licence](LICENSE).

## What's in this repo

A .NET solution (`Inandu.Grid.Extensions.sln`) that turns a request from an
`<inandu-grid serverSide>` into a single paged query.

| Path | What it is |
| --- | --- |
| `src/Inandu.Grid.Extensions/` | the **core** package — `ToInanduGrid()`, zero runtime dependencies |
| `src/Inandu.Grid.Extensions.EntityFrameworkCore/` | `ToInanduGridAsync()` — `CountAsync` + `ToListAsync` |
| `src/Inandu.Grid.Extensions.AspNetCore/` | `[FromInanduGrid]` model binder + minimal-API helpers |
| `tests/` | xUnit tests, one project per `src/` package |
| `benchmarks/` | BenchmarkDotNet project covering each query shape |
| `playground/` | a runnable ASP.NET Core minimal-API app with a real `<inandu-grid serverSide>` front end |
| `docs/` | the request contract and per-feature guides |

The core package must stay **dependency-free** — dynamic sorting and filtering
is built on `System.Linq.Expressions`, not `System.Linq.Dynamic.Core` or
reflection-based query languages. Keep it that way.

## Setup

- **.NET 8 SDK** builds and tests the `net8.0` target.
- **.NET 10 SDK** additionally builds `net10.0` — each `.csproj` adds that target
  framework only when `$(NETCoreSdkVersion)` is ≥ 10. Release packages must be
  produced with the .NET 10 SDK so the `.nupkg`s ship both.

```bash
dotnet restore Inandu.Grid.Extensions.sln
dotnet build   Inandu.Grid.Extensions.sln -c Release
dotnet test    Inandu.Grid.Extensions.sln -c Release
```

CI (`.github/workflows/ci.yml`) runs `build` + `test` on both target frameworks
for every push and pull request; a green run is required to merge.

## Making a change

1. Branch off `main`.
2. Add or update tests for the behaviour you're changing — the test projects
   mirror `src/` one-for-one.
3. If you touch the request contract or an option, update the matching file in
   `docs/` and the `## [Unreleased]` section of [`CHANGELOG.md`](CHANGELOG.md).
4. Run `dotnet test -c Release` locally before opening the PR.
5. Keep commit messages short and descriptive; no automated boilerplate.

## Releasing

Maintainers only — see [`docs/publishing.md`](docs/publishing.md). In short: bump
`<Version>` in the three `src/**/*.csproj` in lock-step, move the CHANGELOG notes
under a dated heading, then `git tag vX.Y.Z && git push --tags`. The
`publish.yml` workflow packs and pushes to nuget.org via trusted publishing.
