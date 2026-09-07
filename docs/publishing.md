# Publishing to NuGet

`InanduGrid.ServerSide` ships to **nuget.org** as a free (MIT) package. It multi-targets
`net8.0` and `net10.0`.

## Prerequisites

- **.NET 10 SDK** — the `.csproj` only adds the `net10.0` target framework when the building SDK is
  ≥ 10 (`$(NETCoreSdkVersion)`). Packing with the .NET 8 SDK produces a **`net8.0`-only** package.
- A nuget.org API key with push rights for the `InanduGrid.ServerSide` id
  (`https://www.nuget.org/account/apikeys`).

## Release checklist

1. Bump `<Version>` in `src/InanduGrid.ServerSide/InanduGrid.ServerSide.csproj` (SemVer).
2. Move the `## [Unreleased]` notes in `CHANGELOG.md` under a new `## [x.y.z] - <date>` heading.
3. Commit, tag: `git tag vX.Y.Z && git push --tags`.
4. Verify from a clean tree:

   ```bash
   dotnet --version                     # 10.x
   dotnet test  -c Release              # all green on net8.0 and net10.0
   dotnet pack  src/InanduGrid.ServerSide/InanduGrid.ServerSide.csproj -c Release -o ./artifacts
   ```

5. Inspect the package:

   ```bash
   dotnet nuget verify ./artifacts/InanduGrid.ServerSide.X.Y.Z.nupkg
   unzip -l ./artifacts/InanduGrid.ServerSide.X.Y.Z.nupkg
   # expect: lib/net8.0/*.dll + *.xml, lib/net10.0/*.dll + *.xml, README.md, the .nuspec
   ```

   Optionally smoke-test the `.nupkg` from a scratch project with a local feed
   (`dotnet nuget add source ./artifacts -n local`).

6. Push the package and its symbols:

   ```bash
   dotnet nuget push ./artifacts/InanduGrid.ServerSide.X.Y.Z.nupkg \
     --api-key <KEY> --source https://api.nuget.org/v3/index.json

   dotnet nuget push ./artifacts/InanduGrid.ServerSide.X.Y.Z.snupkg \
     --api-key <KEY> --source https://api.nuget.org/v3/index.json
   ```

   (`.snupkg` is produced automatically — `IncludeSymbols` + `SymbolPackageFormat=snupkg`.)

7. On nuget.org, confirm the listing shows both `.NET 8.0` and `.NET 10.0` under *Frameworks*.

## Notes

- The package has **no runtime dependencies** — the `.nuspec` dependency groups are empty.
- `RepositoryUrl` points at the private Azure DevOps repo; SourceLink is not wired, so
  step-into-source won't work for external consumers. `PackageProjectUrl` points at the public
  companion Angular library.
- `artifacts/`, `*.nupkg` and `*.snupkg` are git-ignored.
