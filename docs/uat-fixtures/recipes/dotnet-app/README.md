# `dotnet-app` UAT fixture recipe

This directory holds the recipe(s) for building the `dotnet-app` fixture(s) used by the release UAT pass — specifically the registration walk and the detect-strategy coverage matrix in the [recipes README](../README.md#detect-strategy-table-coverage).

Build output lands at `docs/uat-fixtures/build/dotnet-app/` (gitignored). The recipe is checked in; the output is not.

## Build

```bash
# Linux / macOS / WSL
./build.sh

# Windows PowerShell
.\build.ps1
```

Three variants are produced (see "Fixtures the recipe must produce" below). NuGet restore against the configured feeds is required on first run; subsequent runs hit the local cache.

**Reproducibility.** The csproj sets `<Deterministic>true</Deterministic>` and `<PathMap>` to normalise embedded paths; `-p:DebugType=none` strips PDBs from the third variant. Same-machine repeated runs produce byte-identical output for `framework-dependent/UatDotnetFixture.dll` and the self-contained binaries. Bit-for-bit reproducibility across machines requires identical SDK patch versions.

## Fixtures the recipe must produce

| Fixture name | Shape | Purpose |
|---|---|---|
| `framework-dependent/` | A directory containing the publish output of a minimal ASP.NET Core API (`dotnet publish` of a `Microsoft.NET.Sdk.Web` project). The `*.runtimeconfig.json` file MUST be present at the root. The app MUST listen on `$ASPNETCORE_URLS` and serve HTTP 200 on `/` and `/health`. | Drives the happy-path `dotnet-app` registration walk: detect-strategy returns `DotNetRuntimeConfiguration`, port-injection wires `ASPNETCORE_URLS`, health-check probes `/health`. |
| `self-contained/` | A directory containing the publish output of the same minimal API, published with `--self-contained -p:PublishSingleFile=true`. Result: a single `*.exe` (Windows) or extensionless binary (Linux) + a neighboring `*.pdb` + (when ASP.NET) `*.staticwebassets.endpoints.json`. | Drives the `dotnet-app` self-contained code path: `DotnetExtractor` reports `isSelfContained: true`; also serves as the input for the `executable`-fixture-that-looks-like-dotnet case (§ 3.3 / § 4 row "Looks like single-file .NET publish"). |
| `self-contained-pdb-stripped/` | The `self-contained/` recipe, published with `-p:DebugType=none` (no PDB). For the silent-failure regression guard. | Detect-strategy returns `Manual` with empty or single-signal output (`single-file-binary` alone if no `staticwebassets.endpoints.json`). |

## Registration shape the runbook points at

When the runbook says "register a `dotnet-app` with the framework-dependent fixture," the operator types:

- **Artifact location** → absolute path to `docs/uat-fixtures/build/dotnet-app/framework-dependent/`
- **Discovery strategy** → the form pre-populates `DotNetRuntimeConfiguration` from the detect-strategy hint; operator confirms or overrides.
- **Process command** → resolved from the runtime config; operator does not pin manually.

## Recipe constraints

- The minimal API source MUST be small enough that the `DotnetDependenciesData` probe panel surfaces something concrete (at least one notable dep: EF Core or Swashbuckle are typical). A literal `dotnet new web` produces a clean-but-bare fixture; consider adding one package reference for probe-panel coverage.
- The app MUST NOT do any operator-confusing work at startup (no DB seeding, no first-run banners). The point is to be a transparent target for the UAT supervisor.
- The `/health` endpoint MUST return 2xx for the health-check capability default to surface `healthStatus: "healthy"`.
- The recipe scripts (`build.ps1` for Windows, `build.sh` for Linux) MUST be idempotent: running them twice produces the same output directory.

## Cross-OS

The fixture is built per OS (the `*.exe` extension matters for the `executable`-cross-fixture case). Recipes for Windows (`win-x64`) and Linux (`linux-x64`) live alongside each other and the build script picks the right one based on the host.
