# `nodejs-app` UAT fixture recipe

This directory holds the recipe(s) for building the `nodejs-app` UAT fixture(s) consumed by `docs/release-uat.md` § 2 and § 4.

Build output lands at `docs/uat-fixtures/build/nodejs-app/` (gitignored).

## Build

```bash
# Linux / macOS / WSL
./build.sh

# Windows PowerShell
.\build.ps1
```

The fixture server (`sources/with-start-script/server.js`, `sources/no-start-script/server.js`) uses **Node stdlib `http` only** — no runtime dependency on `express`. The `express` entry in `package.json` is declared purely for probe-panel coverage (drives a non-zero `NodeData.dependencies` count + notable list). `node_modules/` is **not** populated by this recipe; the runbook's `nodejs-app` walk does not depend on a working `npm install`. If a future fixture needs an actually-installed dep, populate `node_modules/` in the recipe (vendored or `npm ci`'d against a pinned lockfile).

## Fixtures the recipe must produce

| Fixture name | Shape | Purpose |
|---|---|---|
| `with-start-script/` | A directory containing `package.json` with `"scripts": { "start": "node server.js" }` + a `server.js` that listens on `$PORT` (bare integer, not a URL) and serves HTTP 200 on `/` and `/health`. `node_modules/` populated (either committed or `npm install`'d as part of the recipe). | Drives the happy-path `nodejs-app` registration walk: detect-strategy returns `PackageJson` (fitness `FullMatch`), port-injection wires `PORT` as a bare integer, health-check probes `/health`. |
| `no-start-script/` | A directory containing `package.json` with no `"scripts.start"` entry. | Drives the `Manual` (fitness `LikelyMatch`) detect-strategy path — operator pins the launch command. |
| `malformed-package-json/` | A directory containing a `package.json` that is invalid JSON (e.g. trailing comma, unterminated string). | Drives the silent-failure regression guard per § Silent-failure modes item 10. Detect-strategy must report empty signals + `Manual` (the `JsonException` is caught and treated as "no `package.json`"); without an explicit fixture, this state is indistinguishable from "no `package.json`" at all. |

## Registration shape the runbook points at

When the runbook says "register a `nodejs-app` with the with-start-script fixture," the operator types:

- **Artifact location** → absolute path to `docs/uat-fixtures/build/nodejs-app/with-start-script/`
- **Discovery strategy** → the form pre-populates `PackageJson` from the detect-strategy hint.
- **Launch command** → resolved from `scripts.start`; operator does not pin manually.

## Recipe constraints

- The `server.js` MUST read `process.env.PORT` as a bare integer (`parseInt(process.env.PORT, 10)`), not as a URL. The bare-integer vs. URL discriminator is the test (see runbook § 3.1).
- The `/health` endpoint MUST return 2xx.
- For probe-panel coverage: include at least one notable dep in `dependencies` (e.g. `express`) to drive the `NodeData` package-count assertion above zero. Optionally include `react` and/or `typescript` to exercise the secondary `ReactData` / `TypeScriptData` panels.
- `node_modules/` strategy: the recipe MAY ship `node_modules` pre-installed (faster fixture build, larger repo footprint via the gitignored `build/` dir) OR run `npm install` as part of the build script (cleaner, requires npm on the build host). Pick one and document it in the recipe.
- Recipe scripts MUST be idempotent.

## Cross-OS

`nodejs-app` fixtures are typically OS-agnostic (the `node` runtime handles platform differences). One recipe per fixture, used on both Windows and Linux. The build script can be a single shared script (`build.sh` or `build.ps1`) that produces a platform-independent output dir.
