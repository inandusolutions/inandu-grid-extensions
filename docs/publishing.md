# Publishing to NuGet

Three free (MIT) packages ship to **nuget.org**, all multi-targeting `net8.0` and `net10.0`:

| Package | csproj |
|---|---|
| `Inandu.Grid.Extensions` | `src/Inandu.Grid.Extensions/` |
| `Inandu.Grid.Extensions.EntityFrameworkCore` | `src/Inandu.Grid.Extensions.EntityFrameworkCore/` |
| `Inandu.Grid.Extensions.AspNetCore` | `src/Inandu.Grid.Extensions.AspNetCore/` |

Keep their `<Version>` in lock-step. The two companions depend on the exact same core version they
were built with — bump all three together.

## Prerequisites

- **.NET 10 SDK** — the `.csproj` only adds the `net10.0` target framework when the building SDK is
  ≥ 10 (`$(NETCoreSdkVersion)`). Packing with the .NET 8 SDK produces a **`net8.0`-only** package.
- A nuget.org API key with push rights for the `Inandu.Grid.Extensions` id
  (`https://www.nuget.org/account/apikeys`).

## Release checklist

1. Bump `<Version>` in all three `src/**/*.csproj` (SemVer, same version).
2. Move the `## [Unreleased]` notes in `CHANGELOG.md` under a new `## [x.y.z] - <date>` heading.
3. Commit, tag: `git tag vX.Y.Z && git push --tags`.
4. Verify from a clean tree, then pack every packable project:

   ```bash
   dotnet --version                     # 10.x
   dotnet test  -c Release              # all green on net8.0 and net10.0
   dotnet pack  Inandu.Grid.Extensions.sln -c Release -o ./artifacts
   ```

5. Inspect each package:

   ```bash
   ls ./artifacts/*.nupkg
   unzip -l ./artifacts/Inandu.Grid.Extensions.X.Y.Z.nupkg
   # expect: lib/net8.0/*.dll + *.xml, lib/net10.0/*.dll + *.xml, README.md, the .nuspec
   ```

   Optionally smoke-test from a scratch project with a local feed
   (`dotnet nuget add source ./artifacts -n local`).

6. Push each package and its symbols (core first, then the companions):

   ```bash
   for pkg in ./artifacts/*.nupkg; do
     dotnet nuget push "$pkg" --api-key <KEY> --source https://api.nuget.org/v3/index.json
   done
   ```

   (`.snupkg` files are produced automatically — `IncludeSymbols` + `SymbolPackageFormat=snupkg`.)

7. On nuget.org, confirm each listing shows both `.NET 8.0` and `.NET 10.0` under *Frameworks*, and
   that the companions' dependency on `Inandu.Grid.Extensions` pins the new version.

## Notes

- The **core** package has **no runtime dependencies**. `…EntityFrameworkCore` depends on
  `Microsoft.EntityFrameworkCore` (≥ 8.0.11); `…AspNetCore` uses the ASP.NET Core shared framework
  (`<FrameworkReference>`). Both also depend on the matching `Inandu.Grid.Extensions`.
- `RepositoryUrl` points at the private Azure DevOps repo; SourceLink is not wired, so
  step-into-source won't work for external consumers. `PackageProjectUrl` points at the public
  companion Angular library.
- `artifacts/`, `*.nupkg` and `*.snupkg` are git-ignored.
