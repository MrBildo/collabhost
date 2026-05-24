# Collabhost release UAT runbook

This runbook is the maintainer-facing pre-release verification procedure for Collabhost. It is re-executed every release cycle by one or more operator-bots (or a human maintainer) against a freshly-built release archive, and the result is recorded as a Collaboard card per run on the [Collabhost board](https://collaboard.collabot.dev/collabhost). The UAT gate is **advisory** — a checklist line in `docs/release-process.md` and a release-notes link to the per-run UAT card. There is no CI gate.

The runbook is split into three sections that change at different cadences:

- **Stable contract** — the API surfaces, app-type lifecycles, and silent-failure modes the UAT exercises. Changes when surface contracts change, via normal PR review (a new endpoint that needs a new UAT step lands in the same PR as the endpoint itself, per the fix-in-place rule).
- **Per-release verification** — what is *new* this cut. **Wiped and re-populated each release-cut from the changelog between the last tag and HEAD.** This is the only section the release-cutter touches at cut time.
- **Appendix — host-specific setup** — WSL2 prep, Windows prep, path-layout reference per install mode, CA-trust handling, reset-to-fresh procedure. Touched when host conventions change.

A future `do-collabhost-uat` skill will carry the protocol (session-open invariants, where results go, gate semantics) and drive against this runbook as content. The skill is **not** built yet — it lands after this runbook has converged through one release cycle, so the skill captures the protocol the runbook actually needs in practice. Until then, the "How to use this runbook" section below carries the protocol prelude inline.

---

## How to use this runbook

### Cold-start invariants (protocol prelude)

Before opening Section 1, the operator-bot confirms three things:

1. **You know what you are testing.** The release tag (e.g. `v1.4.0`) OR the `main` SHA the archive was built from. The UAT card body records both — the version under test and the commit SHA of *this runbook* at the time of the run (so `git log -- docs/release-uat.md` answers "what version of the runbook ran").
2. **Your host has no prior Collabhost state.** A fresh-install host (or one reset per the Appendix § Reset-to-fresh procedure). A half-installed previous run pollutes the next run's "fresh install" assertion silently — see the Stable contract § Phase 0.
3. **The UAT card exists in the board's Triage lane before you start.** Title shape: `UAT v1.X.Y — pending` (flip to `pass | fail | partial` at close). Labels: `Infrastructure` plus the `UAT` label (filed via card #346 — if the label doesn't exist yet, `Infrastructure` alone is acceptable for the first run). Creating the card up front means the result has a home before the first assertion fires; partial runs across multiple sessions or operators compose into the same card.

### Where to record results

**One Collaboard card per UAT run.** Per-step receipts in the card body as YAML; raw transcripts inline when short, attached when length forces it. The board is queryable, attachment-capable, and lives on after archive — the right durability shape for a per-release artifact.

The card body uses this schema:

```yaml
context:
  release_target: v1.X.Y
  runbook_sha: <40-char SHA from main>
  host: WSL2-user | WSL2-system | Windows-user | Windows-system
  operator: <bot-name>
  started_utc: <ISO 8601>

results:
  - leg: WSL2-user | WSL2-system | Windows-user | Windows-system
    app_type: dotnet-app | nodejs-app | static-site | executable | n/a
    section: "0" | "1" | "2" | ...   # the Stable contract section anchor
    step: <one-line plain English>
    status: pass | fail | skip | n/a
    duration_ms: <int, optional>
    observed: <JSON/text excerpt or transcript pointer>
    expected: <one-line assertion>
    notes: <free-form; defects, anomalies, "this took 60s instead of 10s">

summary:
  total_steps: <int>
  pass: <int>
  fail: <int>
  skip: <int>
  blocking_failures: [<list of step refs>]
  verdict: pass | fail | partial
```

A step is **blocking** if it is in the §0 pre-flight or §1 fresh-install path (a broken fresh install means the rest of the leg is invalid), or if the maintainer marks it as a release-gate-blocker. A non-blocking failure (e.g. a probe panel renders with slightly different data than expected) does not stop the run — record, continue, and surface in the verdict.

**Markdown gotcha:** Collaboard cards' markdown rendering requires real newlines — literal `\n` in the card body renders as the four-character string, not a line break. Compose the YAML in a real editor.

### Session model — single-bot OR multi-bot

The runbook supports BOTH shapes:

- **Single-bot, one session.** One operator-bot runs all four legs (WSL2-user → WSL2-system → Windows-user → Windows-system) in sequence in one session. The §7 cross-leg sanity assertion fires only in this shape — it requires the same observer across all four legs to compare version strings and boot-time order-of-magnitude.
- **Multi-bot, multi-session.** Different operator-bots run different legs on different hosts across multiple sessions, composing into one card per release. The skill protocol (when built) will say "if a UAT card for this version exists and is partial, resume from it; do not start over." Until then, the convention is: one card per release; each operator appends their leg's results; the last operator flips the verdict.

A full pass across WSL2 × Windows × user × system × five app types is **at least a half-day**, probably more for a first run. Multi-session is the realistic shape; single-session is the exception.

### What "right looks like" — concrete anchors

Each Stable-contract step below records an expected output as a concrete artifact (a JSON shape, a specific log line, a CSS-rendered element). Not "the dashboard loads correctly" — "the dashboard renders a stats strip with four counters: total / running / stopped / crashed, with non-negative integers." A cold operator-bot asserts against a concrete shape; they cannot assert against operator-judgment.

### Pre-flight: release notes template

When the maintainer cuts the release notes (per `docs/release-process.md`), the notes must carry a `UAT card:` slot linking to the UAT card created for this run. This is the visibility mechanism that makes the advisory gate have teeth — a release without a linked UAT card is visible to every operator who reads the notes. (Card #346 tracks the release-notes template change; this runbook documents the slot.)

### Pre-flight: relationship to `install-integration.yml`

A green `install-integration.yml` run is a **precondition** for UAT, not a substitute. Install-integration validates that the install scripts consume the just-shipped Release archives correctly across all RIDs; UAT validates everything install-integration cannot reach — browser-rendered correctness, operator-judgment-class checks, multi-app-type integration, the "fully torn down" assertion. Confirm install-integration ran green on the release tag before starting UAT.

---

## Stable contract

The API surfaces, app-type lifecycles, and silent-failure modes the UAT exercises. This section changes only when surface contracts change — a new endpoint that needs a new UAT step lands in the same PR as the endpoint itself.

### 0. Pre-flight per leg (before any app is registered)

Each leg is one of: **WSL2-user**, **WSL2-system**, **Windows-user**, **Windows-system**. Recommended leg sequence: WSL2-user → WSL2-system → Windows-user → Windows-system. Reasoning: WSL2 has the harder failure modes (systemd-under-WSL2, `loginctl enable-linger`, `setcap`, `ProtectSystem=strict`); user-scope before system-scope on the same OS so the binary is known-good before the privilege-drop layer; foreground before service-managed within an OS so the binary is known-good before the SCM/systemd layer.

**Cleanup between legs is mandatory.** Each leg ends with §8 teardown before the next leg begins, verified by browser-verify that the dashboard at `localhost:58400` is no longer responding. Half-cleaned state contaminates the next leg's "fresh install" assertion (see Appendix § Reset-to-fresh procedure).

Backend pre-flight that the operator checks once per leg, before registering any app:

| What | How to check | Why this exists |
|---|---|---|
| Process binds the configured listen port | `/api/v1/status.listenPort` matches `Hosting:ListenPort` (default 5443; archive default 58400). AND `ss -tlnp` (Linux) / `Get-NetTCPConnection -LocalPort N -State Listen` (Windows) shows Collabhost on that port. | `ListenPortValidator` warns when the bound port disagrees with config — the warning is in Collabhost's own log, not the dashboard. If `ListenPort` is set but Kestrel grabbed a different port (typo, port conflict, env-var override), every subsequent assertion is against the wrong target. |
| `ContentRoot` resolves correctly per install shape | `/api/v1/status.contentRoot` matches the table below. | If `dotnet run` is verifying instead of the installed binary, ContentRoot points at the project dir and every downstream assertion is signal-zero. The S29 cascade (PR #147 / #177 / #277) is the historical anchor — Windows system-scope (SCM-launched) is the most common silent-failure shape because SCM's default cwd is `C:\Windows\System32`. |
| Portal reachable | `/api/v1/status.portalReachable == true` AND a real GET to `https://collabhost.<base-domain>/` returns 200 with body starting `<!DOCTYPE html`. | **Two load-bearing assertions, NOT one.** `proxyState: "running"` is necessary but NOT sufficient — the static-files middleware can have no `wwwroot/` next to the binary and the proxy is still happy. The SPA-shell curl is what catches a packaging regression (silent-failure). |
| `BootVersionTracker` sentinel matches the scenario | `<dataDir>/.last-boot-version` exists. Fresh install: previous-version read returned `"unknown"`. Upgrade: previous-version read returned the prior tag. | If a UAT pass declares itself "fresh install" but the sentinel says otherwise, the install isn't fresh — a previous tree wasn't fully torn down. Catches stale `/var/lib/collabhost/data` on system-scope re-runs. |
| `StartupPreflight` cleared without exit-10 | Service is up; no `exit 10` in `journalctl` / Windows event log. | The data + user-types directories validate before DI builds. Failure exits 10 and `StartupStderr` writes a fatal-startup block + a crash dump under the configured crash-log directory. Verify the crash-log dir is empty on a clean fresh install. |
| `proxyState` reached a terminal state | Poll until `/api/v1/status.proxyState != "starting"` with a bounded timeout, THEN assert `running`. | Microsoft.Hosting.Lifetime's "Now listening on" fires BEFORE `ProxyManager` completes its first config sync. Single-shot status hits during warm-up catch `"starting"` and misread as a real terminal state (silent-failure). The PR #152 fix-up bash poll pattern is the reference shape — pre-condition for every subsequent proxy assertion. |
| Dashboard landed (browser-verify) | Login screen → paste admin key → empty dashboard. Top-bar stats strip shows `System: running` (green dot) AND `Proxy: running` (green dot). App list is empty. | Confirms the SPA shell + auth path + status read all work end-to-end. (INSTALL.md §3 enumerates the proxy states; anything other than `running` is a leg failure.) |
| `/api/v1/version` matches the release archive | The version string in the response matches the version of the archive that was installed. | Mismatch means the wrong archive was installed; stop the leg and verify fixtures. |
| Vendor abstraction holds in the DOM | Grep the rendered DOM (browser-verify) for the literal string `Caddy`. Must NOT appear in any UI-visible text. | The "Proxy not Caddy" rule has no automated enforcement test (see § Cross-cutting backend assertions). The UAT runbook is the natural enforcement point — a leak indicates a frontend regression. (Backend internals and the operator install doc may name Caddy directly; the abstraction boundary stops at the React UI.) |

**ContentRoot expectation table:**

| Install | Expected ContentRoot |
|---|---|
| WSL2 user-scope | `/home/<user>/.collabhost/bin/` (BaseDirectory fallback) |
| WSL2 system-scope | `/opt/collabhost` (systemd unit `Environment=ASPNETCORE_CONTENTROOT=/opt/collabhost`) |
| Windows user-scope | `%USERPROFILE%\.collabhost\bin\` (BaseDirectory fallback) |
| Windows system-scope | `C:\Program Files\Collabhost\bin\` — but SCM's default cwd is `C:\Windows\System32`, so the install MUST set `ASPNETCORE_CONTENTROOT` in `HKLM\SYSTEM\CurrentControlSet\Services\Collabhost\Environment` (REG_MULTI_SZ) or ContentRoot will be `System32`. The SPA shell + SQLite + AppType JSON all land in the wrong place. Load-bearing PR #177 / #277 lesson — **verify the SCM-launched ContentRoot, not the `dotnet run` ContentRoot.** |

**Per-leg stdout / log sink reference:**

| Leg | Stdout sink | Privileged-port story | Admin-key grep |
|---|---|---|---|
| WSL2-user | tmux session stdout, optional `tee` to log file | `setcap cap_net_bind_service=+ep ~/.collabhost/bin/caddy` (INSTALL.md §9.10.1) | `grep 'Collabhost admin key:' <session-log>` |
| WSL2-system | `journalctl -u collabhost --since '5 min ago'` | `AmbientCapabilities=CAP_NET_BIND_SERVICE` (in unit) | `journalctl -u collabhost --since '5 min ago' \| grep 'Collabhost admin key:'` |
| Windows-user | PowerShell stdout, `Tee-Object` to log file | `LocalSystem` not in play; user-mode binds 58400 fine, `:443` needs URL ACL (test-skip acceptable for user-mode) | `Select-String 'Collabhost admin key:' <session-log>` |
| Windows-system | Windows Application Event Log (provider `collabhost*`) | `LocalSystem` binds privileged ports | `Get-WinEvent -LogName Application` filtered per INSTALL.md §5.5.4 |

The admin key is in stdout exactly once on first boot — capture all stdout/stderr to a log file. Record `install.admin_key_captured: true` in the result schema; **never paste the key itself** into the card body.

### 1. Fresh install — common shape per leg

For every leg:

1. **Pre-flight assertions.** Verify §8 teardown ran from the previous leg (no `collabhost` process, no `~/.collabhost/`, no `/opt/collabhost/`, no `%ProgramFiles%\Collabhost\`). If you skipped to this leg mid-runbook, this assertion fails and you stop.
2. **Run the install script.** Capture all stdout/stderr to a log file.
3. **Capture the admin key** per the leg's stdout sink (table above).
4. **Wait for `proxyState=running`** per §0.
5. **Browser-verify dashboard landing** per §0.
6. **Browser-verify `/api/v1/version`** matches the release archive's version string.

### 2. Built-in app types — the five legs of registration

For each of the four install legs, register **all five** built-in app types in this order:

1. **`static-site`** — simplest; no process, no port allocation. If this fails, the proxy / routing stack is broken and the other four will too.
2. **`external-route`** — no process, no port allocation, no artifact. Routing-only against an operator-declared upstream. Exercises the second routing-only shape early so a regression in `BuildReverseProxyRoute`'s `ExternalDial` branch surfaces before the process-bearing types muddy the signal. (Card #348.)
3. **`executable`** — process + port-injection + reverse-proxy, but `Manual` strategy (no discovery). Validates the supervisor surface independent of language-specific discovery.
4. **`dotnet-app`** — process + `DotNetRuntimeConfiguration` discovery + ASP.NET-default env vars.
5. **`nodejs-app`** — process + `PackageJson` discovery + npm-based shape.

This order is "least machinery to most machinery." A failure at step N localizes the bug to the surface added between step N-1 and step N.

#### 2.1 Fixture requirements per app type

Fixtures are built from checked-in recipes at `docs/uat-fixtures/recipes/<app-type>/`. Each recipe's `README.md` describes what the recipe produces; the recipes themselves are scaffolded but not implemented (out of scope for the runbook PR; tracked as follow-up work). Build output lands at `docs/uat-fixtures/build/<app-type>/` (gitignored).

| App type | Fixture shape | Capability surfaces exercised |
|---|---|---|
| `static-site` | A directory containing `index.html` + at least one CSS file + at least one image asset. Optionally `config.json` to exercise `runtime-config-file`. | `FileServer` mode + `responseHeaders` + `runtime-config-file` (when `config.json` present) |
| `external-route` | NO directory — the fixture is a side-process the operator launches on the test box. Suggested: `python -m http.server 11235` from a directory containing an `index.html` + a `health` file (so the configured `/health` probe returns 200). Self-contained; no Docker dependency. The registration target is `host: localhost`, `port: 11235`, `scheme: http`. | `external-target` (host + port + scheme) + `routing` (`ReverseProxy` mode, `ExternalDial` branch) + `health-check` (probes the external target, not localhost-of-Collabhost-process) + `security-headers`. NO process, NO port-injection, NO artifact, NO restart, NO auto-start. |
| `executable` | A directory containing one self-contained binary that listens on `$PORT` and serves HTTP on `/` and a `/health` GET. Tiny Go or Rust binary preferred. | Supervisor + port-injection (`PORT` env var, bare integer) + reverse-proxy |
| `dotnet-app` | An ASP.NET Core publish output directory (`dotnet publish` of a minimal API). The `*.runtimeconfig.json` file at the root is the discoverable signal. | `DotNetRuntimeConfiguration` discovery + `ASPNETCORE_URLS` injection + `health-check` capability default at `/health` |
| `nodejs-app` | A directory containing `package.json` with a `"start"` script + the necessary `node_modules/` (or a recipe to `npm install` before registering). | `PackageJson` discovery + `PORT` injection (bare integer) + `health-check` capability |

**Two additional fixtures for negative-path detect-strategy testing (§4):**

- An "ambiguous" directory (e.g. a `dotnet publish --self-contained` single-file output) — registering this as `executable` should surface the `IsManagedDotnet=true` soft-nudge banner per `ArtifactEvidenceCollector.CollectExecutable`.
- An "empty" directory — every app type's detect-strategy should return `Manual` or `NotApplicable` with no signals.

**Cross-OS path-shape sanity:** the `executable` fixture is the interesting cross-OS case. `*.exe` matters on Windows (the `*.exe` glob branch in `ListExecutablesAtRoot`); the executable bit matters on Linux (the `HasExecutableBit` branch). A fixture without `.exe` on Windows or without `chmod +x` on Linux is invisible to the collector — and that's the test.

**Linux FIFO trap (S33 #220 lesson):** `Directory.EnumerateFiles("/tmp")` on a CI runner can pick up `clr-debug-pipe` FIFOs (extensionless, executable bits set) and a downstream `File.OpenRead` blocks waiting for a writer. The `HasExecutableBit` helper gates pipes/sockets/devices via the zero-length filter. UAT against real fixture dirs won't hit this — documented here so future test-fixture work doesn't re-introduce the regression.

#### 2.2 Registration walk per app type

For each app type, in the dashboard:

1. Navigate to `/apps/new` (App Create page).
2. Step 1: select the app type tile.
3. Step 2: fill the schema-driven registration form (generated from `GET /api/v1/app-types/{slug}/registration`).
4. Use the path-picker to navigate to the fixture directory (or paste the absolute path).
5. Submit.

**Browser-verify after registration:**

- Redirected to `/apps/{slug}` (App Detail page).
- Identity header shows the slug, app type, "Stopped" status.
- For `static-site`: action bar shows "Start" (which toggles route, not a process).
- For the other three: action bar shows "Start" (which starts a process).
- The route preview shows `<slug>.collab.internal` (or whatever `Proxy:BaseDomain` resolves to — record per leg).

**Backend-side assertions per registration:**

- One `App` row written (`Slug` = the form slug, `AppTypeSlug` matches the registered type).
- **Zero `CapabilityOverride` rows** for a default registration — the type-level defaults from `Data/BuiltInTypes/<type>.json` are inherited via `CapabilityResolver.Resolve<T>`. Overrides only appear when the operator changes a setting on the settings page after registration.
- `POST /api/v1/apps` response carries `id` (ULID) AND `writableDataPath` (`<dataDir>/app-data/<slug>` — absolute, runtime-derived, NEVER persisted, per the #326 / #322 E1 decision). Assert `Path.IsPathRooted(writableDataPath)` — a regression in `AppDataPathResolver` that returns relative paths would still serialize cleanly (silent-failure).
- `GET /api/v1/apps/{slug}/settings` returns the section set matching the app-type's capability bindings (varies by type; see per-type assertions below).

**Path-picker dependency (card #344, closed):** `GET /api/v1/filesystem/detect-strategy` accepts `path` as required and `appTypeSlug` as optional. When the slug is provided (the App Create page sets it from the step-1 type-picker), the response is the single-type `DetectStrategyResponse`. When the slug is omitted, the response carries a `perType` map keyed by every AppType the collector has rules for. A form reorder that fills the path before the type is selected no longer 400s — it just gets the per-type map and can render hints for whichever type the operator eventually picks.

**Per-app `/etc/hosts` precondition (card #345):** the documented `install.sh` / `install.ps1` paths handle the Portal hostname (`collabhost.collab.internal`) but **not** per-app subdomains (`<slug>.collab.internal`). The operator must add the per-app entry to `/etc/hosts` (Linux) or `%SystemRoot%\System32\drivers\etc\hosts` (Windows) before §6 routing assertions can pass against the real domain. Operator-UX friction point tracked for future install-script work.

After all five app types are registered, the app list (`AppListPage`) should show five rows with the five slugs. Stats strip on the Dashboard shows `total: 5`. Per D8 (Card #348), the `external-route` app is **auto-enabled at registration** — its row reads `running` (route-enabled) immediately, distinct from the other four types which start `stopped`. Expected snapshot: `total: 5, running: 1 (external-route), stopped: 4`.

### 3. Per-app-type lifecycle assertions

For each registered app, walk the full lifecycle. Steps vary slightly by app type — process-bearing types (`dotnet-app`, `nodejs-app`, `executable`) get start/log-stream/route/health-check/restart/stop/kill/delete; `static-site` (no process) gets start-as-route-toggle/route/stop-as-route-disable/delete.

#### 3.1 Process-bearing app types (`dotnet-app`, `nodejs-app`, `executable`)

**Start:**

- `POST /api/v1/apps/{slug}/start` transitions `ProcessState`: `Stopped` → `Starting` → `Running`. Verify via `/api/v1/apps/{slug}` polling.
- A `ProcessStateChangedEvent` fires for each transition; `ProxyManager` subscribes and triggers route sync on `Running`.
- `/api/v1/apps/{slug}.port` is a non-null int — `PortAllocator` bound a free port via `TcpListener(IPAddress.Loopback, 0)` and assigned it to the process.
- The hosted child inherits Collabhost's parent env PLUS the port-injection variable (port-injection wins last per `MergeEnvironmentVariables` tier ordering, S46 #313 lesson) PLUS the resolved environment-defaults.
- Activity-event row: `app.started` with `actorId` = operator user ID, `appId` = ULID, `appSlug` = slug. Surfaces in `/api/v1/dashboard/events` and `/api/v1/events`.

**Per-app-type port-injection wire shape:**

| App type | Env var name | Value format |
|---|---|---|
| `dotnet-app` | `ASPNETCORE_URLS` | `http://localhost:{port}` (URL form) |
| `nodejs-app` | `PORT` | `{port}` (bare integer) |
| `executable` | `PORT` (default; operator can override) | `{port}` (bare integer) |

The bare-integer vs. URL discriminator is operator-facing. A node app that misreads `PORT` as a URL will fail to bind. Worth exercising explicitly: a fixture that prints its `PORT` env var to stdout; verify the value in the log buffer.

**`dotnet-app` single-file self-contained — bundle-extraction dir (S46 #311):** for a single-file self-contained dotnet-app on Linux system-scope, the per-app bundle-extraction dir is provisioned at `<dataDir>/dotnet-bundle/<slug>/` (`HostedAppBundleDirectory.EnsureFor`). Verify the dir exists post-start AND that `DOTNET_BUNDLE_EXTRACT_BASE_DIR` is set in the hosted child's env to that path. Guards against the #311 regression — under `ProtectHome=true`, the .NET host's default `$HOME/.net` extraction fails with exit 159. The per-app dir under `ReadWritePaths` is the explicit contract.

**Environment-defaults per app type** (resolved capability values, hosted child sees these post-merge):

| App type | Key environment-defaults |
|---|---|
| `dotnet-app` | `ASPNETCORE_ENVIRONMENT=Production`, `DOTNET_ENVIRONMENT=Production`, `DOTNET_NOLOGO=1`, `DOTNET_SYSTEM_CONSOLE_ALLOW_ANSI_COLOR_REDIRECTION=true` |
| `nodejs-app` | `NODE_ENV=production` |
| `executable` | `{}` (no defaults) |

**Log stream check (SSE):**

- On App Detail, the log stream panel populates with stdout/stderr from the launched process via the `use-log-stream` hook.
- Entries arrive with monotonic `id` values.
- Concurrent-streams limit is 10 (`LogStreamEndpoints._maxConcurrentStreams`). Exhausting it returns 503 + `Retry-After: 5`. Exercise once per leg.
- `Last-Event-ID` header OR `?lastEventId=<n>` query resumes from a specific entry. Exercise: connect, capture some logs, drop connection, reconnect with `lastEventId`, assert no gap.
- The `?key=<authKey>` query param is the auth-fallback for SSE (the only endpoint allowed to use this — EventSource cannot set headers).

**Route check:**

- `/api/v1/routes` row: `{ appId, appName: <slug>, appDisplayName, domain: "<slug>.collab.internal", target: "localhost:<port>", proxyMode: "reverseProxy", https: true, enabled: true, isPortal: false }`.
- Caddy admin API (`http://localhost:<adminPort>/config/apps/http/servers/.../routes`) carries a route with `@id: "route_<slug>"`, `match.host` array containing the slug subdomain, and `handle[0].handler: "reverse_proxy"` (or a `subroute` wrap when security-headers are non-empty, #309).
- **Assert both the API row AND the live Caddy admin API state.** The API row is computed at request time from the app's resolved RoutingConfiguration — it reflects intent. Caddy's admin API reflects actual sync state. If route sync failed silently, the API row will say `enabled: true` but the route never landed (silent-failure).
- Wildcard cert covers the slug subdomain — Caddy `tls.certificates.automate` includes `<slug>.collab.internal` in the subject list (Portal subject pinned at index 0, app subjects follow). First HTTPS hit triggers cert issuance via Caddy internal CA + `tls internal`.

**Actual serving:** `curl -k https://<slug>.collab.internal/` returns the fixture's `/` handler with 200. (Per-app `/etc/hosts` precondition per §2.2.)

**Health-check verification (`dotnet-app`, `nodejs-app` only; `executable` has no `health-check` capability by default):**

- `/api/v1/apps/{slug}.healthStatus` populates within ~1.5× the configured `intervalSeconds` (default 30s) after `Running`. Values: `null` (pre-first-probe), `"healthy"`, `"unhealthy"`, `"unknown"`.
- The probe is a localhost GET to `http://localhost:<port>/health` via a private `HttpClient` that bypasses the shared resilience pipeline. Per-call timeout from `health-check.timeoutSeconds`.
- **Assert `healthStatus == "healthy"`, not just `status == "running"`.** The dashboard says "running" if the process is alive; `healthStatus: "unhealthy"` means the app responded non-2xx to `/health`. The two are independent (silent-failure).

**Restart:**

- `POST /api/v1/apps/{slug}/restart` transitions `Running` → `Stopping` → `Stopped` → `Starting` → `Running`. `RestartCount` increments by 1. Activity-event row: `app.restarted`.
- **Port may change.** `PortAllocator.AllocateAsync` re-binds. Assert the new port AND that the Caddy route's `target` was re-synced to the new port (event-bus subscription on `ProcessStateChangedEvent`). Stale route target post-restart is a silent-failure mode (event-bus subscription dropped).

**Stop:**

- `POST /api/v1/apps/{slug}/stop` transitions `Running` → `Stopping` → `Stopped`. The operator-stopped flag is set (`StoppedByOperator = true`) — auto-restart does NOT fire after operator stop.
- The process group / Job Object / cgroup is reaped. PID is `null` post-stop.
- Caddy route is preserved by default. Confirm `/api/v1/routes.<slug>.enabled == true` post-stop AND that hitting `https://<slug>.collab.internal/` returns 502 — the route is up but no upstream is the operator-visible "stopped" signal.
- Activity-event row: `app.stopped`.

**Kill:**

- `POST /api/v1/apps/{slug}/kill` SIGKILLs the process (no graceful shutdown window). State: `Running` → `Stopped`. `LastExitCode` reflects the kill signal.
- Containment is reaped (cgroup.kill atomic + `Process.WaitForExit` before rmdir per S39 #219 lesson; Windows Job Object closes and reaps the process tree).
- Activity-event row: `app.killed`.

**Delete:**

- `DELETE /api/v1/apps/{slug}` runs the stop-then-delete sequence with a **10-second timeout**. If the process doesn't stop in 10s, `KillAppAsync` runs as fallback.
- Cleanup on success:
  - `App` row removed.
  - All `CapabilityOverride` rows for the app (cascade delete).
  - `ProcessSupervisor` removes the process from `_processes`, ring buffer from `_logBuffers`, restart policy from `_restartPolicies`.
  - Probe cache entry invalidated (`ProbeService.InvalidateProbeCache(app.Id)`, ULID-keyed — re-registration with the same slug gets a fresh entry, S56 #337 fix-along).
  - Per-app bundle directory reaped (`HostedAppBundleDirectory.Reap(appSlug)` — slug-keyed). Best-effort: failure to reap is logged but not surfaced.
  - Caddy route torn down via event subscription on `ProcessStateChangedEvent`.
- **NOT cleaned up (documented behavior, not a bug):**
  - Activity-log rows for the app — historical activity is preserved. Confirm `/api/v1/events?appId=<deleted-app-id>` still returns the historical events.
  - The per-app writable data path (`<dataDir>/app-data/<slug>/`) — Collabhost did not create this dir; the operator may have used the `writableDataPath` from the registration response to put a SQLite DB there. Collabhost won't delete files it didn't create.

#### 3.2 `static-site` (no process — route toggle)

**Start (no process):**

- `POST /api/v1/apps/{slug}/start` does NOT spawn a process. It calls `ProxyManager.EnableRoute(<slug>)` — Caddy gets a `file_server` handler at `<slug>.collab.internal` pointing at `artifactConfiguration.Location`.
- **No `ProcessState` transition fires.** Static-site has no `ManagedProcess`; `/api/v1/apps/{slug}.status` is computed from `hasRouting && routeEnabled` via `ResolveStatus`. Assert `status == "running"` reflects route-enabled, not a process state.
- **No port allocation.** `port` is `null`.
- **Silent-failure mode:** "Start" reports success but `EnableRoute` failed (e.g., Caddy admin API unreachable, `ProxyState.Degraded`). The API row says `enabled: true` (operator intent), the Caddy admin API shows no route. Assert both. The S46/S47 `proxyState: "degraded"` field exists precisely for this case.

**Routing:**

- Caddy route `@id: "route_<slug>"` with `handle[0].handler: "file_server"`, `handle[0].root` pointing at the artifact dir.
- `runtime-config-file` capability emits a Caddy response-headers handler for `/config.json` (cache-control: no-cache, per type default). **Folded into the per-app settings walk** — when the fixture includes a `config.json` and the operator sets non-empty values on the `runtime-config-file` capability, verify the file is materialized at `<artifactDir>/config.json` post-start with the resolved values (#336).
- For `spaFallback = true`: Caddy serves `index.html` for any 404. Exercise once with a deep-link request returning the SPA shell.

**Actual serving:** `curl -k https://<slug>.collab.internal/` returns the fixture's `index.html`.

**Restart-from-stopped:** `POST /api/v1/apps/{slug}/start` again. Verify route re-appears, content serves.

**Stop:**

- `POST /api/v1/apps/{slug}/stop` calls `ProxyManager.DisableRoute(<slug>)`. The route is replaced with a "disabled" shape (`BuildDisabledRoute`) that returns 503 or similar — the @id stays on the same route but the handler tells operators "this app is stopped."
- Hitting `https://<slug>.collab.internal/` post-stop returns the disabled-route response, NOT a 502 (the file_server is replaced, not torn down — the @id-keyed route persists). Silent-failure: a regression where DisableRoute removes the route entirely would surface as "the app is stopped" but Caddy reports 502 instead of the disabled shape. The disabled-route shape is the explicit signal.

**Restart / Kill:** Static-site has neither (`AppActions.CanRestart: false`, `CanKill: false`). Assert the API rejects these calls with the expected error message (not a 500).

**Delete:** Same path as process-bearing types. Route is torn down. Artifact dir is NOT deleted by Collabhost (operator owns the artifact).

#### 3.3 `executable` — when the binary looks like .NET

When the `executable` fixture is a self-contained `.NET` publish (the ambiguous fixture from §2.1), the per-app detail probe panel surfaces a "consider re-registering as dotnet-app" nudge per `ArtifactEvidenceCollector.LooksLikeDotnetBinary` and the `ExecutableData.IsManagedDotnet` field.

**Expected banner wording (string fixture — freeze the assertion):**

> Consider re-registering as dotnet-app

(If the wording changes between releases, the UAT step still passes on a rendered banner with arbitrary text — the assertion is unmoored without the literal-string check. The string fixture is the assertion. When the wording legitimately changes, update this section as part of the same PR.)

Assert: the banner renders AND its text content matches the string above (case-sensitive).

#### 3.4 `external-route` (no process — route to a service Collabhost does not run)

`external-route` is the second routing-only AppType. Unlike `static-site`, the route points at an operator-declared upstream `host:port` instead of a local artifact directory. Lifecycle is identical to `static-site` (enable/disable route, no start/stop process) with one difference: per D8, the route is **auto-enabled at registration**, so `status == "running"` from the moment the operator gets a 200 back from `POST /api/v1/apps`.

**Pre-flight: side process running.** Before registration, the operator launches the fixture upstream on the test box. With the suggested Python fixture: `cd <fixture-dir> && python -m http.server 11235` in a separate tmux pane / PowerShell window. Verify `curl http://localhost:11235/health` returns 200 from the test box before registering. If the upstream isn't reachable from the box at registration time, the health probe will start firing `unhealthy` (terminology-rendered as `Unreachable` per D6) and the operator chases a Collabhost bug that isn't there.

**Registration (private-only by default — D3):**

- Register via dashboard `/apps/new` → 6th tile (`External Route`) → fill `external-target.host`, `external-target.port`, `external-target.scheme`.
- Submit with `host: localhost`, `port: 11235`, `scheme: http`. Expect 200; redirect to `/apps/<slug>`.
- Activity log: `app.created` event written (per Kai's #348 PR-230 review C-1, the absence of an `app.started` event on auto-enable is documented behavior, not a regression — record what is observed without asserting `app.started`).

**Negative path — private-only validation rejects a public host (D3):**

- Attempt to register a second external-route with `host: api.openai.com`. With `ExternalTargetSettings.AllowPublicHosts == false` (the default), the API rejects with a 400 carrying `ExternalTargetHostPatternMessage` ("Host must be localhost, 127.0.0.1, ::1, an RFC1918 / link-local IPv4 address, or a *.local / *.lan hostname. To front a public hostname, set ExternalTarget:AllowPublicHosts = true in appsettings.").
- Update `appsettings.json` to set `"ExternalTarget": { "AllowPublicHosts": true }`. Restart Collabhost (per §6.3 per-leg restart shape).
- Re-attempt the `api.openai.com` registration. Expect 200. Note: the fallback `PermissiveHostnamePattern` still rejects whitespace and structurally-invalid hosts — try `host: " not valid "` to confirm.
- **Tear down the opt-in before continuing:** revert the `AppSettingsSection` change; restart. The default-private posture is the supported steady state for the rest of the UAT pass.

**Route emission (the F-1 / F-2 surface — verify directly):**

- `GET /api/v1/routes` row for the slug: `target` is the operator-declared `host:port` (e.g. `localhost:11235`), **NOT** `"not-running"` or `localhost:0`. Per Kai's #348 PR-230 review F-1 (folded into the polish round), the `target` column synthesis honors `ExternalDial` for external-route apps. A regression that surfaces `"not-running"` on the Routes page for a live external-route is the exact symmetric-bug the F-1/F-2 fix-along closed.
- MCP `list_routes` returns the same shape — `target: "localhost:11235"`. Mirror assertion to F-2.
- Caddy admin API (`http://localhost:<adminPort>/config/apps/http/servers/.../routes`) carries the route with `@id: "route_<slug>"` and `handle[0].handler: "reverse_proxy"`, `handle[0].upstreams[0].dial: "localhost:11235"` — NOT `"localhost:0"`. **The dial pre-resolution is the load-bearing change.**
- For an `https` upstream fixture (skip on standard UAT; exercise once when a TLS upstream is available): the emitted Caddy config carries the `transport` block with an empty `tls` object (Card #348 D2).

**Actual serving:** `curl -k https://<slug>.collab.internal/` returns the Python `http.server` directory listing. Per-app `/etc/hosts` precondition per §2.2.

**Health check (D6 terminology split):**

- `/api/v1/apps/{slug}.healthStatus` populates within ~1.5× the configured `intervalSeconds` (default 30s). The backend enum value is `healthy / unhealthy / degraded / unknown` (unchanged from managed apps — the wire shape is uniform per the cross-tier discipline).
- **Browser-verify the rendered label.** On App Detail, the Health tab + the stats-strip Health cell render `Reachable` / `Unreachable` / `Degraded` / `Unknown` for external-route — NOT `Healthy` / `Unhealthy`. The split is FE display formatting (`formatHealthStatus(status, appTypeSlug)` in `lib/format.ts`). A regression that renders `Healthy` for an external-route is a frontend bug (the formatter discriminator failed).
- Probe target verification: the probe URL is `http://localhost:11235/health`, NOT `http://localhost:<some-collabhost-port>/health`. The split between supervised-process probes (probe Collabhost's allocated port) and external-target probes (probe the operator-declared host:port) is the §9 health-check refactor. Capture the probe's stdout on the Python fixture to confirm — the fixture should log a GET to `/health` on the cadence.
- **Disabled-route → probe halts.** Disable the route (`POST /api/v1/apps/{slug}/stop` → `EnableRoute(slug) == false`). The probe stops firing (gate-condition in `HealthCheckExecutorService.TickAsync`); the `latest` cache for this app is cleared; `healthStatus` reads `null`. Re-enable; probes resume.

**`tabs` DTO drives App Detail tab strip (D5):**

- Navigate to `/apps/{slug}`. The tab strip shows exactly two tabs: `Health` and `Route`. The `Logs` and `Technology` tabs are **absent** — external-route has no process to stream logs from and no probe extractors that apply.
- Stats strip shrinks: shows `Uptime` + `Health` only. The process-bound cells (PID / Port / Restarts / Memory) are absent — there is no process to measure.
- Health tab content: renders the terminology-split label with about-this-probe copy. Route tab content: renders Domain / Upstream / TLS rows with the operator-declared values.
- A regression that surfaces `Logs` / `Technology` tabs for external-route is a frontend bug (the `tabs` field was ignored).

**Type picker (D7):**

- Navigate to `/apps/new`. The type-picker grid shows 6 tiles (the 5 prior types + `External Route`). The tile description text reads "Reverse-proxy route to a service Collabhost does not manage (Docker, LAN host, Tailscale, self-hosted upstream)." (or whichever copy is on the wire from `Data/BuiltInTypes/external-route.json` `description`).
- A regression that omits the tile means `GET /api/v1/app-types` is not returning the new built-in (the embedded resource load broke).

**Restart-from-stopped:** `POST /api/v1/apps/{slug}/start` again. Verify route re-appears, the upstream serves through Caddy, the probe resumes.

**Restart / Kill:** External-route has neither (`AppActions.CanRestart: false`, `CanKill: false`). Assert the API rejects these calls with the expected 409 InvalidOperationException-mapped error (not a 500).

**Delete:** Same path as the other types. The route is torn down explicitly via the §13 fix-along (`AppEndpoints.DeleteAppAsync` calls `proxy.DisableRoute(slug)` + `RequestSync()` before the EF delete, closing the small window where Caddy still routes to a deleted app). For MCP `delete_app`, Kai's PR-230 review C-2 noted the symmetric fix-along did NOT land in MCP — `RegistrationTools.DeleteAppAsync` does not call `DisableRoute`. Test the REST delete path on the runbook; surface a finding if a follow-up makes the MCP path symmetric.

### 4. Detect-strategy suggestion verification

`GET /api/v1/filesystem/detect-strategy?path=<absolute>&appTypeSlug=<slug>` returns `DetectStrategyResponse(SuggestedStrategy, paths[])` per `FilesystemEndpoints.DetectStrategy`. The strategy comes from `ArtifactEvidenceCollector.Collect`. (The `appTypeSlug` is optional per card #344; omitting it returns a `perType` map keyed by every AppType the collector has rules for. The per-app-type assertions below exercise the slug-provided shape.)

**Expected suggestion per app type × fixture shape:**

| App type | Fixture | Expected `SuggestedStrategy` | Signal kinds |
|---|---|---|---|
| `dotnet-app` | Framework-dependent publish (`*.runtimeconfig.json` at root) | `DotNetRuntimeConfiguration` | `runtime-config` |
| `dotnet-app` | Source dir (`*.csproj` at root, no publish output) | `DotNetProject` | `project-file` |
| `dotnet-app` | Self-contained single-file publish (`*.exe` + `*.pdb`) | `Manual` | `single-file-binary` + `pdb-pair` |
| `dotnet-app` | Self-contained single-file publish with static assets (`*.exe` + `*.pdb` + `*.staticwebassets.endpoints.json`) | `Manual` | `single-file-binary` + `pdb-pair` + `static-asset-manifest` |
| `dotnet-app` | Self-contained single-file with PDBs stripped (no `staticwebassets`) | `Manual` (fitness `LikelyMatch` or empty) | `single-file-binary` alone, OR empty signals (PR #223 #329 K-1 — see silent-failure modes) |
| `dotnet-app` | Mixed: `*.runtimeconfig.json` AND `*.csproj` at root | `DotNetRuntimeConfiguration` (first-match wins) | `runtime-config` |
| `dotnet-app` | Empty / unrecognized | `Manual` (fitness `NotApplicable`) | `[]` |
| `nodejs-app` | `package.json` with `start` script | `PackageJson` (fitness `FullMatch`) | `package.json` |
| `nodejs-app` | `package.json` without `start` script | `Manual` (fitness `LikelyMatch`) | `package.json` |
| `nodejs-app` | Malformed `package.json` | `Manual` + empty signals (`JsonException` caught, treated as "no `package.json`") | `[]` — silent-failure: indistinguishable from "no `package.json`" without an explicit corrupt-fixture test |
| `nodejs-app` | No `package.json` | `Manual` (fitness `NotApplicable`) | `[]` |
| `static-site` | Has `index.html` | `NotApplicable` (fitness `FullMatch`) | `index-html` |
| `static-site` | Has `index.htm` or `default.html` | `NotApplicable` (fitness `LikelyMatch`) | `index-html` |
| `static-site` | `*.html` files at root, no `index.html` | `NotApplicable` (fitness `LikelyMatch`) | `html-files` |
| `static-site` | No HTML files | `NotApplicable` + empty signals | `[]` |
| `executable` | Single `*.exe` at root (Windows) | `Manual` (fitness `FullMatch`) | `binary-at-root` with `count: 1` |
| `executable` | Single extensionless executable with user-execute bit (Linux) | `Manual` (fitness `FullMatch`) | `binary-at-root` |
| `executable` | Multiple executables at root | `Manual` (fitness `LikelyMatch`) | `binary-at-root` with `count: N`, `binaryName` = first sorted |
| `executable` | Looks like single-file .NET publish | Signal attribute `isManagedDotnet: true` — surfaces the §3.3 nudge | varies |
| `executable` | No executables | `Manual` + empty signals | `[]` |
| (any) | Empty directory | `Manual` (or `NotApplicable` for static) | `[]` |
| `external-route` | (no path field — no `artifact` capability) | N/A — the registration form has no discovery section because the `external-target` capability supplies the upstream directly. The detect-strategy endpoint is not called. | N/A |

**Verification flow (per app type, per leg):**

1. **API direct.** `curl 'http://localhost:58400/api/v1/filesystem/detect-strategy?path=<fixture>&appTypeSlug=<slug>'` (with `X-User-Key` header) returns the expected JSON shape. Record `actual.suggestedStrategy` and `actual.paths` for diff against expected.
2. **Form surface.** Browser-verify the App Create page — when the operator types or selects the fixture path in the registration form's path-picker, the form pre-populates the discovery strategy with the suggestion. Browser-verify the dropdown is set to the right value (e.g. `DotNetRuntimeConfiguration` not `Manual` for the standard dotnet fixture).

### 5. Probe verification

Each app type emits probe data per `Probes/_ApiContracts.cs`. The frontend renders these on App Detail in probe panels.

**Expected probe panel per app type:**

| App type | Probe entries | Panel shape on App Detail |
|---|---|---|
| `dotnet-app` | `DotnetRuntimeData` (TFM, runtime version, IsAspNetCore, IsSelfContained, ServerGc) + `DotnetDependenciesData` (package count + notable list) | "Runtime" panel + "Dependencies" panel |
| `nodejs-app` | `NodeData` (engine, package manager, module system, dep counts) + optionally `ReactData` / `TypeScriptData` if detected | "Node" panel + "React" panel (if React detected) + "TypeScript" panel (if TS detected) |
| `static-site` | `StaticSiteData` (HasIndexHtml, HtmlFileCount, TotalAssetBytes, HasNestedAssets) | "Static Site" panel |
| `executable` | `ExecutableData` (BinaryName, BinarySizeBytes, CandidateBinaryCount, IsManagedDotnet) | "Executable" panel + nudge banner if `IsManagedDotnet` (see §3.3 for the frozen banner wording) |
| `external-route` | — | **No probe panel.** External-route has no artifact directory to extract from; the `tabs` field omits `technology`, so the probe section never renders. `probesStatus` is `not-applicable`. The Technology tab is absent from the AppDetail tab strip entirely — there is no empty-state copy because there is no surface to render the empty state into. |
| `system-service` | — | **Out of scope for this runbook today.** `system-service` is a registered AppType but no fixture is in the §2.1 set. Probe contract returns `ProbeCacheStatus.NotApplicable`. If a future release adds probe behavior for system-service apps, extend §2.1 + this table in the same PR. Placeholder anchor for the eventual addition. |

#### 5.1 The four `ProbeCacheStatus` states

Per `_ApiContracts.cs`:

- `NotApplicable` — AppType not in probe set (only `system-service` today). UI renders distinctly from "not yet probed."
- `NeverProbed` — no cache entry. Either brand-new (< periodic-tick), or periodic sweep hasn't completed first run.
- `Fresh` — cache entry within freshness window. May have empty `Entries` legitimately (e.g. dotnet directory contains nothing recognizable).
- `Stale` — cache entry outside freshness window. Under normal operation should NEVER appear — if it does, periodic refresher is wedged.

**UAT walks the observable states:**

- Register a new app → assert `NeverProbed` within first ~10s.
- Wait for periodic tick → assert transition to `Fresh`.
- `Stale` is **accepted as unobserved in v1.x** — the state requires stopping the periodic service to produce, and no test-knob exists to do so yet. **Card #343 tracks the future test-knob.** Runbook documents the gap.

**Silent-failure mode:** the extractor throws during evidence collection but the periodic service catches and logs only. `probesStatus = "fresh"` with `probes = []` is the legitimate empty extraction; `probesStatus = "stale"` means the periodic loop has stopped advancing — the operator should see explicit signal. **The UAT MUST check `probesStatus`** even if it doesn't introspect the panel contents. A regression where every probe extractor errors silently surfaces as "the panels are empty" — `probesStatus = "fresh" + probes = []` looks identical from the frontend to "this app has no extractable runtime info" without the status field (#337 / PR #214).

#### 5.2 Browser-verify each panel

For each registered app, navigate to `/apps/{slug}` and assert:

- Each expected panel renders with non-zero data (where the fixture provides extractable data).
- The data matches a manually-computed expectation (e.g. for the static-site fixture, `HtmlFileCount=1` if the fixture has only `index.html`).
- For the `executable` fixture built as self-contained .NET, the `IsManagedDotnet=true` nudge banner is visible with the frozen wording per §3.3.

### 6. Routing and auto-start

#### 6.1 Activity log assertion

After all five apps complete §3 lifecycle, `GET /api/v1/events` should carry an event sequence per app — `registered` → `started` → (health-check-passed?) → `stopped` → `restarted` → `deleted`. Browser-verify the events feed on Dashboard renders the recent activity.

For static-site: `app.started` fires even though there's no process — the activity event represents intent + route enablement.

For external-route: per Kai's #348 PR-230 review C-1 (intentional, documented behavior), **`app.started` does NOT fire on auto-enable at registration** — only `app.created` is written. Subsequent manual stop → start cycles DO record `app.stopped` / `app.started` per the normal route-toggle path. If a future release reverses C-1 (folds the missing event into the auto-enable path), update this paragraph in the same PR.

For process-bearing apps: `app.crashed` for any app the operator expects to be running, `app.fatal` for an app that exhausted its backoff window, and `app.killed` when the operator did NOT issue a kill (suggests the supervisor force-killed via the 10s timeout on delete or graceful-shutdown fallback). Any of these on a clean UAT pass is a finding.

#### 6.2 Auto-start verification

After all five apps are started AND `dotnet-app`/`nodejs-app`/`static-site` have `auto-start.enabled=true` per their JSON, **restart Collabhost itself** (the binary, not just the apps). On boot:

- `dotnet-app`, `nodejs-app`, `static-site` should auto-start. Browser-verify they're running within ~30s of Collabhost coming back.
- `executable` (auto-start default `false`) should remain stopped.
- `external-route` has NO `auto-start` capability binding (per Card #348 spec §4) — but its route-state is in-memory only and defaults to enabled on first read. Confirm a previously-running external-route is back to `running` post-Collabhost-restart (route-enabled by default-true semantics, not by auto-start). A previously-stopped external-route is also `running` post-restart because the in-memory `false` from `DisableRoute` did not persist. **This is a known asymmetry with the four process-bearing types** — stopped state across restart only survives for apps with persisted lifecycle state. Not a regression; record per leg and surface to operators in the runbook narrative below.
- An `app.auto_started` activity event fires for each auto-started app (distinct from `app.started`). Check `/api/v1/events?type=app.auto_started` post-restart. External-route does NOT emit `app.auto_started` (no auto-start capability binding) — the route silently becomes reachable again.

**Static-site auto-start:** static-site does NOT have an `auto-start` capability binding in the type defaults — but auto-start of a static-site means re-enabling the Caddy route, not starting a process. Confirm a started static-site is restored to `running` (route-enabled) post-Collabhost-restart.

#### 6.3 Per-leg restart shape (auto-start verification step)

The "restart Collabhost itself" step in §6.2 has a different mechanic per leg. Use the right one for the leg under test:

| Leg | Restart command |
|---|---|
| WSL2-user | Stop the tmux session, start a new one with the same `collabhost` invocation. |
| WSL2-system | `sudo systemctl restart collabhost` |
| Windows-user | Stop the foreground process (Ctrl-C or close window), re-launch from the same install dir. |
| Windows-system | `Restart-Service Collabhost` (PowerShell, elevated) OR `sc.exe stop collabhost && sc.exe start collabhost`. |

Each shape has its own correctness story (does the per-app DB carry the right pre-restart state? does the SIGHUP reload service interfere?). Use the documented shape per leg — an operator-bot can get clever and use the wrong one.

### 7. Cross-leg sanity (single-bot single-session only)

This section is only worth running if the same operator-bot ran all four legs in one session. If the four legs ran across multiple sessions or multiple operators, skip this — the per-leg results stand on their own.

- **Version-string consistency.** All four legs should report the same `GET /api/v1/version` (since they install the same archive per RID — different RIDs may differ on patch-level RID build but should not on Collabhost version). Record per leg.
- **Boot-time order-of-magnitude.** Record `install.duration_seconds` and `boot.first-200-from-status-seconds` per leg. Outliers (e.g. Windows-system taking 60s when the others take 10s) are surfaceable.

### 8. Teardown (every leg ends with this)

No exceptions, no "we'll clean up later." The cross-leg pollution failure mode (a half-cleaned `~/.collabhost/` on WSL2 contaminating a "fresh" WSL2-system install) is real and silent.

#### 8.1 Per-app cleanup (automatic via DELETE)

§3.1/§3.2 delete steps already remove every app + its registry / probe / log / containment state. Verify with `ls` after the last DELETE — the per-app `data/app-data/<slug>` and `data/dotnet-bundle/<slug>` directories should be reaped.

#### 8.2 Per-leg Collabhost teardown

| Leg | Teardown |
|---|---|
| WSL2-user | Stop the tmux session running collabhost. `rm -rf ~/.collabhost/`. If `setcap` was set on the bundled Caddy, the directory removal reaps it. |
| WSL2-system | `sudo systemctl disable --now collabhost && sudo rm /etc/systemd/system/collabhost.service && sudo systemctl daemon-reload && sudo rm -rf /opt/collabhost /etc/collabhost /var/lib/collabhost /var/log/collabhost && sudo userdel collabhost` (INSTALL.md §5.5.2 uninstall recipe). |
| Windows-user | Stop the foreground process. Remove the install directory + `%USERPROFILE%\.collabhost\`. |
| Windows-system | `.\install-system.ps1 -Uninstall -PurgeData` (or the manual recipe in INSTALL.md §5.5.4). |

#### 8.3 CA-trust cleanup

If the bundled Caddy's internal-CA root cert was imported into the OS / browser trust store (INSTALL.md §9.11), remove it. Otherwise the next UAT run starts with the previous run's CA cert lurking. See Appendix § CA-trust handling for per-host commands.

#### 8.4 Negative-path verification (positive checklist of things that should NOT exist)

After teardown, all of these MUST be true:

- `curl http://localhost:58400/api/v1/status` → connection refused (process gone).
- `curl -k https://<slug>.collab.internal/` → DNS fail or proxy-gone for every previously-registered slug.
- `which collabhost` → not found (if it was on `PATH`).
- File-system: `ls ~/.collabhost/` (WSL2-user), `ls /var/lib/collabhost/` (WSL2-system), `ls $env:ProgramData\Collabhost` (Windows-system) — all error or empty.
- Process list: no `collabhost` or `caddy` processes from the prior run.
- Cert store: no `caddy-internal` entries (if previously trusted).

A failed negative-path assertion is a leg-result blocker. See Appendix § Reset-to-fresh procedure for the "I cannot get back to clean" escape hatch.

### Silent-failure modes (consolidated)

These are the assertions where the operator surface looks green but the backend is wrong. Every UAT pass MUST exercise the assertion that surfaces the failure, not the operator-facing surface alone.

1. **`status: "running"` AND `healthStatus != "healthy"`.** Dashboard says green; app is broken. Assert `healthStatus` (§3.1).
2. **`status: "running"` AND `pid` is set BUT the process is orphaned.** Containment cleanup broke. Verify by killing the supervisor; surviving children indicate the containment didn't enclose (§ Cross-cutting).
3. **`/api/v1/routes` shows the route but Caddy admin API doesn't.** Sync silently failed. Assert both (§3.1).
4. **`probesStatus: "fresh"` AND `probes: []` for an artifact known to have extractable data.** Extractor errored silently. Assert that for a known-extractable artifact, `probes` is non-empty (§5.1).
5. **Detect-strategy returns empty signals on a real PDB-stripped self-contained .NET 10 publish that lacks `staticwebassets.endpoints.json`.** The bundle-signature fallback is dead against .NET 10 (`SingleFileBundleReader.TryRead` verified empirically in PR #223 / #329 K-1). Operator hits Manual entry. Cover with the explicit fixture (§4 row 5).
6. **Restart succeeds but Caddy still points at the old port.** Event-bus subscription dropped. Assert `/api/v1/routes.<slug>.target` reflects current port (§3.1).
7. **`proxyState: "running"` AND the SPA shell is unreachable.** Static-files middleware has no `wwwroot/`. Assert SPA shell curl returns `<!DOCTYPE html` (§0). NOTE: a partially-stripped `wwwroot/` (e.g. assets present but `index.html` missing, or vice versa) is **not assertable today** — card #342 tracks this gap; the runbook catches only fully-stripped state.
8. **Static-site "start" succeeds but `EnableRoute` failed (Caddy admin reachable, load failed).** `proxyState` degrades to `"degraded"`. Assert `proxyState == "running"`, not just `enabled: true` (§3.2).
9. **A dotnet-app under hardened systemd "starts" but exits 159 immediately.** `DOTNET_BUNDLE_EXTRACT_BASE_DIR` not set or pointed at unwritable path. Assert via the activity-event sequence (`app.started` then `app.crashed` within seconds) (§3.1).
10. **Malformed `package.json` looks like "no `package.json`" to detect-strategy.** Cover with the explicit corrupt-fixture step (§4 row 10).
11. **Stale @id collision in Caddy after delete-then-recreate.** Cover with the cycle: register → start → stop → delete → register-with-same-slug → start. Assert clean Caddy state at each step (§ Cross-cutting).
12. **External-route Routes-page row reads `target: "not-running"` for a live route (Card #348 F-1 / F-2 mirror).** Pre-#348-polish, the route-table target column synthesized `localhost:{port}` from the supervised process — for external-route with no process, that fell through to `"not-running"` even when the route was emitting `dial = "{host}:{port}"` correctly. The polish round closed it; the runbook continues to assert. Cover by registering an external-route, opening the Routes page, asserting the `target` column reads the operator-declared `host:port` (NOT `not-running` and NOT `localhost:0`). The same assertion fires against MCP `list_routes`.
13. **External-route health probe reports `healthy` while the upstream is actually down.** The probe is a localhost-style GET from the Collabhost process to the operator-declared `host:port`. If a process on the test box is bound to the port but is not the intended upstream (port collision or a leftover side-process), the probe returns 200 and reads `Reachable`. The operator's `*.collab.internal` request reaches the same misbound process via Caddy. **Silent-failure surface: the dashboard says green but the upstream is wrong.** Mitigation: the `/health` endpoint on the fixture must be specific enough that a port-collider won't accidentally serve it. The Python `http.server` fixture serves a literal `health` file from the upstream's directory — a port-collider that doesn't have that file in CWD returns 404 and the probe correctly reads `Unreachable`. Document, do not engineer around.
14. **`ExternalTargetSettings.AllowPublicHosts = true` accepted but the host is not validated against the fallback shape.** When `AllowPublicHosts == true`, the strict private-only regex is bypassed but the `PermissiveHostnamePattern` (`^[A-Za-z0-9.-]+$`) is still enforced. A regression that bypasses BOTH patterns would accept arbitrary input including whitespace, control characters, or shell-metacharacters into the host field. Cover with the explicit "whitespace host" and "shell-meta host" rejection-still-fires assertion in §3.4.

### Cross-cutting backend assertions

Beyond per-app-type lifecycle, these contracts apply across every leg.

#### Process containment cleanup

**Windows Job Objects (Windows legs):**

- On start, `WindowsJobObjectContainment.CreateContainer(<slug>)` creates a Job Object; the spawned process is assigned. The Job Object has `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE` — closing the handle kills every process in the job.
- **Verify on kill / crash / orphan:** any child processes the hosted app spawned are reaped along with the parent. Exercise an app that forks a sleeper child, kill the parent, assert no orphan via `Get-Process -Name <child>`.
- **Verify on supervisor death:** kill `collabhost.exe` itself (`Stop-Process -Force`). All managed processes die because Job Object closure cascades. Re-launch Collabhost; no stale PIDs.

**Linux cgroup v2 (WSL2 legs):**

- On start, `LinuxContainment.CreateContainer(<slug>)` provisions `/sys/fs/cgroup/collabhost.slice/<slug>.scope/`. The hosted child is moved into that cgroup.
- On kill / stop: `cgroup.kill` is atomic, but membership reaping is async — `Process.WaitForExit()` (or poll `cgroup.procs` for emptiness) before `rmdir` (S39 #219 lesson). A failed reap shows up as EBUSY on rmdir; the warning is logged.
- **Verify orphan-kill watcher:** the `setpriv --pdeathsig SIGTERM` watcher fires on supervisor death and writes "1" to `cgroup.kill`. Exercise by SIGKILLing the supervisor and asserting that cgroup membership empties + the apps die. WSL2's systemd is the faithful repro environment we validated against (S46 #311).

**Cross-platform regression:** the runbook MUST exercise crash + kill + supervisor-restart on BOTH Linux (real cgroup) and Windows (real Job Object). Cross-platform process-runner differences are load-bearing — `WindowsProcessRunner` uses CreateProcess P/Invoke + `GenerateConsoleCtrlEvent` for graceful shutdown; `LinuxProcessRunner` uses native fork/exec via `LinuxNativeMethods` with cgroup containment; `FallbackProcessRunner` is `System.Diagnostics.Process` (used when neither native runner applies). Each runner has subtly different exit-code surfacing, env-var precedence, and grace-period behavior.

#### Port allocation race / collision

`PortAllocator.FindFreePort()` is a bind-to-zero pattern. There is a TOCTOU window between port release and the hosted child's bind — under heavy concurrent starts, two apps could race onto the same port. Exercise rapid-fire concurrent starts of 5+ apps and assert every app got a distinct port AND every app is `Running` (no port-conflict crash).

#### Caddy route sync on rapid start/stop cycles

The proxy uses a Channel-based sequential processor (`Channel.CreateBounded<bool>(1)` with capacity 1). Rapid state transitions coalesce into a single sync — the channel write is `TryWrite` style. Exercise rapid start/stop/start cycles and assert the FINAL route state matches the final intent.

**Stale @id collisions:** if the @id is `"route_<slug>"` and a previous route with the same @id wasn't fully torn down before the next sync, Caddy returns an error on the load. Cover register → start → stop → delete → register-new-with-same-slug → start. Assert clean Caddy state at each step.

#### Crash-restart exponential backoff

- `GetBackoffDelay()` is `min(2^(consecutiveFailures-1), 60)` seconds. After 10 consecutive failures (`HasMaxRestartsExceeded`), the app transitions to `Fatal` and does NOT restart further.
- **Recovery window:** after 5 minutes of `Running`, `ShouldResetRestartCount` returns true and the next crash resets the counter.
- **UAT walks 2-3 backoff cycles + asserts eventual `Fatal`.** Do not walk the full ~10-minute timeline (Bill ruling D15). Use a contrived fixture — an executable that exits 1 immediately — and observe `Running` → `Crashed` → `Backoff` (with increasing delays) → after 2-3 cycles, fast-forward by acknowledging the contract holds and asserting that `app.fatal` is the terminal event after 10 failures via a unit-test-style fixture (or accept the partial-walk + the activity-event sequence `app.crashed, app.auto_restarted, ...` as evidence).

#### Boot version tracking

- `<dataDir>/.last-boot-version` is the previous-boot sentinel. Read on startup, written after preflight clears. Malformed contents fall back to `"unknown"`.
- **First-run scenario:** sentinel absent → `Read` returns `"unknown"`. Write fires after preflight. Assert the file appears in the data dir with the current version.
- **Upgrade scenario:** sentinel carries the prior tag → `Read` returns that tag. Used for migration detection. Today no version-gated migrations exist, but the contract is load-bearing — a regression that makes the sentinel unreliable surfaces on the next migration card.

#### `environment-defaults` capability tier ordering (S46 #313)

`MergeEnvironmentVariables`: capability-variables → `IProcessEnvironmentProvider` (unconditional overwrite, secret source-of-truth) → port-injection (wins last).

Exercise: set `ASPNETCORE_URLS=http://localhost:99999` on a dotnet-app via the settings page; confirm the hosted child sees the port-injected value, NOT the override. Then exercise an `environment-defaults` override (set `ASPNETCORE_ENVIRONMENT=Development`) and confirm both apply.

#### `writableDataPath` contract (#326 / #322 E1)

`GET /api/v1/apps/{slug}.writableDataPath` is `<dataDir>/app-data/<slug>` (absolute, derived from the resolve-once `effectiveDataDir`). NEVER persisted — recomputed per request. Confirm by restarting Collabhost and verifying the value is identical (good — it's derived) AND that the dir is NOT created by Collabhost (the operator creates it on first use). Contract: "Collabhost tells you where to write; you decide whether to write."

Exercise the operator-side: point an app's SQLite connection string at `writableDataPath/db.sqlite` via the settings page, restart the app, assert the DB file appears under `<dataDir>/app-data/<slug>/db.sqlite`. This is the E1 contract's whole point — the runbook is the operator-facing proof it works on hardened systemd.

#### SIGHUP reload (Linux only)

`kill -HUP <pid>` triggers `TypeStore.ReloadAsync("SIGHUP", ...)`. Operator-facing: edit a file in the user-types dir, send SIGHUP, the new type appears in `/api/v1/app-types`. The default handler is suppressed (we want SIGHUP for reload, not controlled shutdown). Exercise once on WSL2 (either leg) and verify (a) the type reload happened, (b) the supervisor did NOT shut down.

#### MCP UAT — out of scope here

The MCP surface (`/mcp` endpoint, the `RegistrationTools`/`LifecycleTools`/`ConfigurationTools`/`DiscoveryTools`/`ActivityLogTools` toolset) is a parallel control plane to the REST API. MCP UAT is **out of scope for this runbook** (Bill ruling D11) — a separate runbook + future card will cover it. The MCP tool surface uses its own auth filter (`McpAuthentication`) and bypasses the standard auth middleware, so the UAT shape differs enough to warrant a separate doc.

### Boundary cases worth explicit coverage

- **Slug collisions.** Registering an app with a slug that collides with the Portal subdomain (`collabhost` is the Portal's default) — what's the response? Today: probably allowed at the registry level but the Portal route wins because it's pinned at index 0. Exercise one assertion.
- **Slug edit attempts.** `App.Slug` is immutable post-registration. Attempt a settings-page edit; assert the field is non-editable AND that the API rejects any direct PUT carrying a slug change.
- **Out-of-tree artifact references.** `dotnet-app` registered with `process.discoveryStrategy = "Manual"` and the operator's command is `dotnet /opt/somewhere-else/MyApp.dll`. Discovery only validates strategy syntax, not path-rooting against artifact — exercise one step.
- **Privileged port (Linux).** A hosted app trying to bind to port 80 / 443 under a non-root supervisor. Port-injection picks an ephemeral, but a Manual-strategy app whose command bakes in port 80 would crash on bind. Documented note.

### Backend telemetry / log lines

The keyed signals indicating "things are correct" or "something is off":

**Structured-log keys worth a grep:**

- `"Process supervisor starting -- checking for auto-start apps"` — supervisor entry.
- `"Auto-starting app '{DisplayName}'"` — auto-start path.
- `"Process supervisor started"` — auto-start completed.
- `"Failed to auto-start app"` — auto-start failure (silent-failure: logged but does NOT halt Collabhost — check on every boot).
- `"Restart policy '{RestartPolicy}' for '{DisplayName}' -- restarting after {Delay}s"` — crash-restart confirmation.
- `"Health check executor started -- tick interval"` — health-check service entry.
- `"SIGHUP reload handler registered (kill -HUP {Pid} to force user-type reload)"` — Linux-only.
- `"Last-boot-version sentinel at {Path} has malformed contents"` — sentinel rot.
- `"Failed to ... write last-boot-version sentinel"` — disk-write failure (silent-failure on upgrade detection).
- `"Proxy admin API not reachable within 5s"` — proxy probe failure.
- `"Proxy sync degraded"` — `proxyState: "Degraded"`. Operator-facing.
- Any `Microsoft.Hosting.Lifetime` level prefix from `warn:|fail:|crit:` — NOT `error:` / `critical:` / `fatal:` (S29 lesson; PR #152). The journal-grep set is `warn:|fail:|crit:`.

---

## Per-release verification

> **This section is wiped and re-populated at every release-cut from the changelog between the last tag and HEAD.**
>
> **For the initial draft (release of v1.0.0+1 or whichever is next after this runbook lands): leave this placeholder in place. The first real population happens at the next release cut.**
>
> The release-cutter writes only into this section. The Stable contract above changes via normal PRs as surface contracts change. This section is the per-release delta.
>
> **Population recipe (for the release-cutter):**
>
> 1. Get the conventional-commit log between the last tag and HEAD: `git log <last-tag>..HEAD --pretty=format:'- %s'`.
> 2. Filter to commits that touched API surfaces, capability JSON, app-type registrations, install scripts, or operator-facing behavior. Skip purely-internal refactors.
> 3. For each surviving commit, write one bullet: "**verify [the change works]** — [how to verify]."
> 4. List any temporary migration smoke (a one-time check that doesn't belong in the Stable contract because it only applies to upgraders of *this* release).
> 5. Link the release notes (which carry the "UAT card:" slot per #346).
>
> When the release ships and the UAT card is closed, this section is wiped at the start of the next release cycle.

### What's new to verify this cut

*(empty — placeholder for the first real population)*

### Temporary migration smoke

*(empty — placeholder for the first real population)*

---

## Appendix — host-specific setup

### WSL2 prep (one-time per host)

- **Distro.** Ubuntu 22.04+ or Debian 12+.
- **systemd.** Enable systemd in WSL2 (`/etc/wsl.conf` → `[boot] systemd=true`, then `wsl --shutdown` from Windows).
- **tmux.** `sudo apt install tmux`. Verify `which tmux` resolves. The tmux MCP server is the only way to host a long-lived `collabhost` foreground process from this harness inside WSL2; `wsl bash -c` cannot host a foreground service.
- **`loginctl enable-linger`** for the user-scope leg. Without this, the user's systemd session terminates on logout and `systemctl --user` units stop.

### Windows prep (one-time per host)

- **PowerShell 7+.** The project default. Verify with `pwsh --version`.
- **Administrator-elevated PowerShell capability.** Required for the Windows-system leg.
- **Browser with browser-verify support.** Reachable from the Windows install at `http://localhost:58400`.

### Path layout reference per install mode

| Install mode | Binary | Config | Data | Logs |
|---|---|---|---|---|
| WSL2-user | `~/.collabhost/bin/collabhost` | `~/.collabhost/appsettings.json` | `~/.collabhost/data/` | tmux stdout |
| WSL2-system | `/opt/collabhost/collabhost` | `/etc/collabhost/appsettings.json` | `/var/lib/collabhost/data/` | `journalctl -u collabhost` |
| Windows-user | `%USERPROFILE%\.collabhost\bin\collabhost.exe` | `%USERPROFILE%\.collabhost\appsettings.json` | `%USERPROFILE%\.collabhost\data\` | PowerShell stdout |
| Windows-system | `%ProgramFiles%\Collabhost\bin\collabhost.exe` | `%ProgramData%\Collabhost\appsettings.json` | `%ProgramData%\Collabhost\data\` | Windows Event Log (provider `collabhost*`) |

### CA-trust handling

The bundled Caddy generates an internal-CA root cert. If you imported it into the OS / browser trust store to make `<slug>.collab.internal` reachable from another device (INSTALL.md §9.11), remove the trust entry during teardown. Otherwise the next UAT run starts with the previous run's CA cert lurking.

| Host | Trust store | Removal |
|---|---|---|
| Linux (system) | `/etc/ssl/certs` + `update-ca-certificates` | `sudo rm /usr/local/share/ca-certificates/caddy-internal.crt && sudo update-ca-certificates --fresh` |
| Windows | `cert:\LocalMachine\Root` | `Get-ChildItem cert:\LocalMachine\Root \| Where-Object {$_.Subject -match 'Caddy'} \| Remove-Item` |
| Browser (Firefox, separate store) | per-profile | Open `about:preferences#privacy` → View Certificates → remove |

### Reset-to-fresh procedure

If a leg's pre-flight assertion finds prior Collabhost state on the host, reset before continuing. This is the manual reset procedure; a future `collabhost --uninstall-completely` flag would replace this with a single command (out of scope for the runbook; surfaced for a future card).

**Per leg, in order:**

1. Run the leg's teardown command from §8.2 (uninstall script or manual recipe).
2. Verify §8.4 negative-path assertions pass (every "should not exist" check returns the expected absence).
3. If a CA cert was previously trusted, remove it per § CA-trust handling.
4. If `/etc/hosts` (or Windows `hosts`) has entries from prior runs for `<slug>.collab.internal`, remove them — leftover entries pointing at `127.0.0.1` won't actively hurt the next run, but they fail the "fully torn down" claim.
5. Confirm the data dir for the leg is gone (`ls /var/lib/collabhost/` returns ENOENT; etc.).
6. If the host is genuinely unrecoverable (mixed prior states, files locked, partial uninstall), the escape hatch is a fresh OS install or a VM snapshot reset. Don't fight a dirty host — UAT against a clean host is the whole point.

### UAT-failure post-mortem policy

A UAT failure produces a card-and-fix in the normal flow when UAT catches the regression *before* the release ships. A **post-mortem is required only when a regression escaped UAT into a release** — i.e., a release shipped with a green UAT card and an operator hit a defect the UAT should have caught. The discrimination matters because the first case is the UAT working as intended; the second is a process failure (the UAT didn't fire, or the UAT didn't catch what it should have).
