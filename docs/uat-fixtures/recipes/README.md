# UAT fixture recipes

Reproducible build recipes for the fixtures used by the release UAT pass. One subdirectory per hosted app type:

| Recipe | Variants produced | Source toolchain |
|---|---|---|
| [`static-site/`](./static-site/) | `basic/`, `with-config-json/`, `spa-bundle/` | pure data (no build) |
| [`nodejs-app/`](./nodejs-app/) | `with-start-script/`, `no-start-script/`, `malformed-package-json/` | Node stdlib only at runtime |
| [`dotnet-app/`](./dotnet-app/) | `framework-dependent/`, `self-contained/`, `self-contained-pdb-stripped/` | .NET SDK 10.x |
| [`executable/`](./executable/) | `single-binary/`, `multiple-binaries/`, `looks-like-dotnet/` | Go 1.21+ (+ dotnet-app for `looks-like-dotnet`) |
| [`external-route/`](./external-route/) | `localhost-http/` | pure data; the tester launches `python3 -m http.server` from the built dir |

## Build all five

Each recipe ships a `build.sh` (Linux/macOS/WSL) and a `build.ps1` (Windows PowerShell) pair. Run the recipes for whichever fixtures the UAT leg under test will exercise.

Suggested order (the `executable/looks-like-dotnet/` variant depends on the dotnet-app self-contained output):

```bash
# Linux / macOS / WSL
docs/uat-fixtures/recipes/static-site/build.sh
docs/uat-fixtures/recipes/nodejs-app/build.sh
docs/uat-fixtures/recipes/external-route/build.sh
docs/uat-fixtures/recipes/dotnet-app/build.sh
docs/uat-fixtures/recipes/executable/build.sh
```

```powershell
# Windows PowerShell
.\docs\uat-fixtures\recipes\static-site\build.ps1
.\docs\uat-fixtures\recipes\nodejs-app\build.ps1
.\docs\uat-fixtures\recipes\external-route\build.ps1
.\docs\uat-fixtures\recipes\dotnet-app\build.ps1
.\docs\uat-fixtures\recipes\executable\build.ps1
```

## Output

All recipe output lands at `docs/uat-fixtures/build/<app-type>/<variant>/` and is gitignored (see the repo-root `.gitignore`). The recipes themselves are tracked; the build outputs are not.

## Reproducibility

| Recipe | Run-to-run stability | Cross-machine stability |
|---|---|---|
| `static-site` | byte-identical | byte-identical (pure committed data) |
| `nodejs-app` | byte-identical | byte-identical (pure committed data) |
| `external-route` | byte-identical | byte-identical (pure committed data) |
| `dotnet-app` | byte-identical (`<Deterministic>true</Deterministic>` + `<PathMap>`) | requires identical SDK patch version + identical NuGet cache state |
| `executable` (Go binary) | byte-identical (`-trimpath -buildvcs=false -ldflags='-buildid='`) | requires identical Go toolchain version |

All recipes pin mtimes to `2026-01-01T00:00:00Z` post-build so downstream archive hashes are stable even when filesystem timestamps would otherwise differ.

## Platform verification scope

All five recipes were run and smoke-tested on **Windows PowerShell** (the authoring environment). `build.sh` scripts were verified for correct shebang and LF line endings on all five types.

**git-bash:** `build.sh` for `static-site` and `executable` executed under git-bash and produced **byte-identical output** to the PowerShell runs. This is a meaningful Linux-shell datapoint — git-bash runs the POSIX script path and relies on the same toolchain (Go, file I/O) that a native Linux runner would.

**Not yet verified:** native-Linux-runner execution of the full recipe set (an actual ubuntu-latest CI run or WSL2 invocation of all five `build.sh` scripts end-to-end). Deferred to the next UAT execution dispatch. The `dotnet-app` and `nodejs-app` scripts are the highest-risk for environment-specific behavior (SDK discovery, npm cache paths) and should be prioritized in that run.

## Detect-strategy table coverage

Each fixture exercises a row of the detect-strategy contract — the rules the registration form's strategy detector applies to an artifact path. Coverage matrix (each detect-strategy case mapped to a fixture-output path):

| Detect-strategy case | Fixture path |
|---|---|
| `dotnet-app` framework-dependent (`*.runtimeconfig.json` at root) | `build/dotnet-app/framework-dependent/` |
| `dotnet-app` self-contained single-file (`*.exe` + `*.pdb`) | `build/dotnet-app/self-contained/` |
| `dotnet-app` self-contained + static assets | `build/dotnet-app/self-contained/` (`UatDotnetFixture.staticwebassets.endpoints.json` ships) |
| `dotnet-app` self-contained with PDBs stripped (no `staticwebassets`) | `build/dotnet-app/self-contained-pdb-stripped/` |
| `dotnet-app` source dir (`*.csproj` at root) | **deferred** — the UAT pass walks the publish-output path, not the source-dir path. Re-evaluate when a leg explicitly exercises `DotNetProject` discovery. |
| `dotnet-app` mixed (`*.runtimeconfig.json` AND `*.csproj`) | **deferred** — same reasoning. |
| `dotnet-app` empty / unrecognized | any empty dir; recipe-free. |
| `nodejs-app` with `start` script | `build/nodejs-app/with-start-script/` |
| `nodejs-app` without `start` script | `build/nodejs-app/no-start-script/` |
| `nodejs-app` malformed `package.json` | `build/nodejs-app/malformed-package-json/` |
| `nodejs-app` no `package.json` | any empty dir; recipe-free. |
| `static-site` has `index.html` | `build/static-site/basic/` (also `with-config-json/` and `spa-bundle/`) |
| `static-site` has `index.htm` or `default.html` | **deferred** — secondary index variants are not yet exercised by the UAT pass; add when a leg calls for them. |
| `static-site` `*.html` files at root, no `index.html` | **deferred** — same reasoning. |
| `static-site` no HTML files | any empty dir; recipe-free. |
| `executable` single `*.exe` (Windows) / single `+x` binary (Linux) | `build/executable/single-binary/` |
| `executable` multiple executables at root | `build/executable/multiple-binaries/` |
| `executable` looks like single-file .NET publish | `build/executable/looks-like-dotnet/` |
| `executable` no executables | any empty dir; recipe-free. |
| `external-route` (no path field) | not applicable — the registration form has no discovery section. Fixture is the side-process. |
