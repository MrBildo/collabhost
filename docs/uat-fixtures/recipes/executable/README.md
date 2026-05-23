# `executable` UAT fixture recipe

This directory holds the recipe(s) for building the `executable` UAT fixture(s) consumed by `docs/release-uat.md` § 2, § 3.3, and § 4. The recipes themselves are **not yet implemented** — this README describes what they must produce. Building them out is tracked as a follow-up card.

Build output lands at `docs/uat-fixtures/build/executable/` (gitignored).

## Fixtures the recipe must produce

| Fixture name | Shape | Purpose |
|---|---|---|
| `single-binary/` | A directory containing exactly one self-contained binary at the root. On Windows: `<name>.exe` (the `.exe` extension is what `ListExecutablesAtRoot` keys off). On Linux: extensionless binary with user-execute bit (`chmod +x`). The binary MUST listen on `$PORT` (bare integer) and serve HTTP 200 on `/` and `/health`. Tiny Go or Rust binary preferred — small footprint, no runtime dependencies. | Drives the happy-path `executable` registration walk: detect-strategy returns `Manual` (fitness `FullMatch`) with `binary-at-root` signal (`count: 1`); supervisor pins the operator-supplied command; port-injection wires `PORT`. |
| `multiple-binaries/` | A directory containing two or more executables at the root. | Drives the `Manual` (fitness `LikelyMatch`) detect-strategy path: `binary-at-root` signal with `count: N`, `binaryName` = first sorted (ordinal). Operator picks the launch binary explicitly. |
| `looks-like-dotnet/` | A symlink to / copy of the `dotnet-app/self-contained/` build output, registered as `executable` instead of `dotnet-app`. | Drives the § 3.3 `IsManagedDotnet` nudge: the probe panel surfaces the "Consider re-registering as dotnet-app" banner. The fixture exists so the runbook can assert the banner renders with the frozen wording. |

## Registration shape the runbook points at

When the runbook says "register an `executable` with the single-binary fixture," the operator types:

- **Artifact location** → absolute path to `docs/uat-fixtures/build/executable/single-binary/`
- **Discovery strategy** → `Manual` (the only `executable` strategy).
- **Launch command** → operator pins explicitly: e.g. `./<binary-name>` on Linux, `<binary-name>.exe` on Windows. The form pre-populates from the `binary-at-root` signal when possible.

## Recipe constraints

- The binary MUST read `process.env.PORT` (or equivalent) as a bare integer.
- The `/health` endpoint MUST return 2xx. NOTE: `executable` has no `health-check` capability by default — the runbook does not assert `healthStatus` for `executable` registrations. The `/health` endpoint exists for parity with other fixtures and is exercised only if the operator manually enables a `health-check` override.
- The binary MUST exit cleanly on SIGTERM (Linux) / Ctrl-Break (Windows) within ~5s. Supervisor graceful-shutdown timeouts depend on this.
- For the `multiple-binaries/` fixture: two distinct binaries with deterministic sort order. `aaa` and `bbb` (or `app-one.exe` and `app-two.exe`) — the first by ordinal sort is what the `binaryName` signal will name.
- For the `looks-like-dotnet/` fixture: the artifact is the `dotnet-app/self-contained/` build output. The recipe SHOULD symlink (or copy) from there rather than rebuild — single source of truth.
- Recipe scripts MUST be idempotent.

## Cross-OS

The `executable` fixture is the **interesting** cross-OS case (runbook § 2.1):

- Windows: the `*.exe` extension is what `ListExecutablesAtRoot` matches on.
- Linux: the user-execute bit (`HasExecutableBit`) is what gates the match.

A fixture without `.exe` on Windows or without `chmod +x` on Linux is invisible to the collector — and that's the test. Build per-OS, store in `docs/uat-fixtures/build/executable/single-binary-win/` and `docs/uat-fixtures/build/executable/single-binary-linux/` (the gitignored output dir; the operator picks the right one for the leg under test).

## Linux FIFO trap reference

`Directory.EnumerateFiles("/tmp")` on a CI runner can pick up `clr-debug-pipe` FIFOs (extensionless, executable bits set) and a downstream `File.OpenRead` blocks waiting for a writer (S33 #220). The `HasExecutableBit` helper gates pipes/sockets/devices via the zero-length filter. UAT against real fixture dirs won't hit this — flagged here so a future test-fixture recipe doesn't accidentally re-introduce the regression by, for example, dropping a named pipe into the fixture dir.
