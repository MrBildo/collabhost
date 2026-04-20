# Settings Audit — Collabhost Platform

**Date:** 2026-04-18
**Branch:** `spec/153-release-pipeline`
**Auditor:** Recon drone (read-only)
**Purpose:** Card #159 — settings-resolution architecture decision feed

---

## Methodology

Searched the full backend (`Collabhost.Api/`, `Collabhost.AppHost/`, `Collabhost.ServiceDefaults/`) and frontend (`frontend/`) using:

- Pattern grep for `IConfiguration`, `IOptions<`, `Configure<`, `GetSection`, `GetValue<`, `Bind(`, `Environment.GetEnvironmentVariable`, `import.meta.env`, `VITE_`, `process.env`
- Glob sweep of all `appsettings*.json`, `launchSettings.json`, `*.csproj`, `vite.config.ts`, `.env*`, `Directory.Build.props`
- Direct reads of all `*Registration.cs` / `Program.cs` files in every subsystem
- Read all capability configuration classes and built-in type JSON files

**Limits / ambiguities:**
- `appsettings.Local.json` is gitignored and not present in the worktree. Its schema is implied from `appsettings.json` but actual values are operator-defined.
- The `Platform:ToolsDirectory` setting is present in `appsettings.json` but no C# code reads it through `IOptions` or `IConfiguration` in the codebase (confirmed by grep). It may be vestigial or reserved. Flagged in Observations.
- Aspire's `AddViteApp` + `WithReference(api)` inject `services__api__http__0` into the Vite dev server process environment, but this happens at Aspire framework level — there are no explicit `WithEnvironment()` calls in `AppHost/Program.cs`.
- Tests were not audited as a settings source (they override settings via `WebApplicationFactory`, but those are test fixtures, not platform settings).

---

## Settings Inventory

### 1. Hosting / Process

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| HTTP listen port (standalone) | `launchSettings.json` profile `http` → `applicationUrl` | `http://localhost:58400` | ASP.NET Core startup | Used when running standalone, not via Aspire |
| HTTPS listen port (standalone) | `launchSettings.json` profile `https` → `applicationUrl` | `https://localhost:58401;http://localhost:58400` | ASP.NET Core startup | Dev-only profile |
| ASPNETCORE_ENVIRONMENT | `launchSettings.json` (API and AppHost both set) | `"Development"` | ASP.NET Core runtime | Controls migration-on-startup, CORS, OpenAPI, TypeStore loading |
| DOTNET_ENVIRONMENT | `launchSettings.json` (AppHost only) | `"Development"` | Aspire/runtime | Set on AppHost; not set on Api launchSettings directly |
| ASPIRE_ALLOW_UNSECURED_TRANSPORT | `launchSettings.json` (AppHost) | `"true"` | Aspire | Enables HTTP in Aspire dashboard transport |
| DOTNET_ASPIRE_SHOW_DASHBOARD_RESOURCES | `launchSettings.json` (AppHost) | `"true"` | Aspire dashboard | Shows all resources in the Aspire dashboard |
| Aspire AppHost HTTP port | `AppHost/Program.cs` hardcoded → `.WithEndpoint("http", e => e.Port = 58400)` | 58400 | Aspire framework | Port 58400 pinned in AppHost; also repeated in Api launchSettings |
| Aspire AppHost application URL | `launchSettings.json` (AppHost) | `http://localhost:15889` (http), `https://localhost:17889` (https) | Aspire | Dashboard/DCP listen URLs |
| DOTNET_DASHBOARD_OTLP_ENDPOINT_URL | `launchSettings.json` (AppHost) | `http://localhost:19889` | Aspire OTel collector | Where the Aspire dashboard expects OTLP pushes |
| DOTNET_RESOURCE_SERVICE_ENDPOINT_URL | `launchSettings.json` (AppHost) | `http://localhost:20889` | Aspire DCP | Resource service endpoint |
| Vite dev server port | `vite.config.ts` hardcoded | 5173 | Vite | Not configurable from env |

---

### 2. Data / Storage

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| SQLite connection string / DB path | `appsettings.json("ConnectionStrings:Host")` > hardcoded fallback | `"Data Source=./db/collabhost.db"` | `Data/_Registration.cs` → `configuration.GetConnectionString("Host")` | Fallback string is hardcoded in `DataRegistration`; the appsettings value and the fallback are identical, making the appsettings key effectively a no-op override path |
| User types directory | `appsettings.json("TypeStore:UserTypesDirectory")` > hardcoded fallback | `"UserTypes"` | `Data/AppTypes/_Registration.cs` → `TypeStoreSettings` | Resolved relative to `AppContext.BaseDirectory` unless rooted. If `TypeStore` section is absent, defaults to `"UserTypes"` (hardcoded in the null-coalescing fallback) |
| Tools directory | `appsettings.json("Platform:ToolsDirectory")` | `"tools"` | **Not read by any code** — see Observations | Present in config but no consumer found |

---

### 3. Auth

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| Admin key (configured) | `appsettings.json("Auth:AdminKey")` = `null` > `appsettings.Local.json` (operator override) | `null` | `Authorization/_Registration.cs` → `Configure<AuthorizationSettings>` | When null, a ULID is generated at startup and logged. The generated key is ephemeral (not persisted). |
| Admin key (generated fallback) | `PostConfigure<AuthorizationSettings>` — generated ULID at startup | runtime-generated ULID | `Authorization/_Registration.cs` | Logged as WARNING; printed to stdout. Ephemeral — regenerates on every restart |
| Admin DB seed key | `IOptions<AuthorizationSettings>.AdminKey` (same resolved value) | Same as above | `Authorization/UserSeedService.cs` | Used to seed the first admin user row if DB is empty. Has its own null-fallback inside `StartAsync` (identical generator path) |
| Auth key resolver | `IOptionsMonitor<AuthorizationSettings>` | — | `Authorization/AuthKeyResolver.cs` | Reads the live config value; supports hot-reload via `IOptionsMonitor` |
| Auth skip paths | Hardcoded array `["/health", "/alive", "/openapi", "/mcp"]` | — | `Authorization/AuthorizationMiddleware.cs` | Not configurable |
| SSE query-param auth path | Hardcoded suffix check `"/logs/stream"` | — | `Authorization/AuthorizationMiddleware.cs` | Only path where `?key=` is accepted |
| GET /status bypass | Hardcoded (GET + exact path match) | — | `Authorization/AuthorizationMiddleware.cs` | Public endpoint, no key required |
| Auth storage key (frontend) | Hardcoded string constant | `"collabhost-user-key"` | `frontend/src/lib/constants.ts` | localStorage key for auth token |

---

### 4. Proxy

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| Base domain | `appsettings.json("Proxy:BaseDomain")` | `"collab.internal"` | `Proxy/_Registration.cs` → `ProxySettings` | Required (`required` keyword); throws if section missing |
| Caddy binary path | `appsettings.json("Proxy:BinaryPath")` | `"caddy"` | `Proxy/_Registration.cs` → `ProxySettings` | Bare name = PATH resolution via `where`/`which`; path with sep = absolute/relative |
| Caddy listen address | `appsettings.json("Proxy:ListenAddress")` | `":443"` | `Proxy/ProxyConfigurationBuilder.cs` | Used as Caddy `servers.srv0.listen` value |
| TLS cert lifetime | `appsettings.json("Proxy:CertLifetime")` | `"168h"` | `Proxy/ProxyConfigurationBuilder.cs` | Caddy internal CA cert TTL |
| Self port (Collabhost → Caddy self-route) | `appsettings.json("Proxy:SelfPort")` | `58400` | `Proxy/ProxyConfigurationBuilder.cs` | Port Caddy reverse-proxies back to Collabhost API |
| Caddy admin API port | **Not configurable** — `PortAllocator.AllocatePort()` at startup | Ephemeral OS-assigned | `Proxy/_Registration.cs` | Allocated fresh each boot, written into a temp bootstrap JSON, injected as `--config`. Never persisted |
| Caddy admin base URL | Constructed from `AdminPort` at startup | `http://localhost:{ephemeral}` | `Proxy/_Registration.cs` → `HttpClient<CaddyClient>` | Hardcoded scheme/host; only port varies |
| Proxy admin bootstrap config path | `Path.GetTempPath()` + `"collabhost/caddy-bootstrap.json"` | System temp dir | `Proxy/ProxyArgumentProvider.cs` | Not configurable |
| Route domain template | `{slug}.{baseDomain}` — resolved from `RoutingConfiguration.DomainPattern` + `ProxySettings.BaseDomain` | `"{slug}.{baseDomain}"` per built-in type JSONs | `Proxy/ProxyConfigurationBuilder.cs`, `Mcp/ConfigurationTools.cs` | Template baked into type binding JSON with `{baseDomain}` token substituted at TypeStore load time |
| Route sync startup delay | Hardcoded `TimeSpan.FromSeconds(2)` | 2 seconds | `Proxy/ProxyManager.cs` | Delay after proxy process Running to allow Caddy admin API to become ready |
| PKI CA name | Hardcoded `"Collabhost Local Authority"` | — | `Proxy/ProxyConfigurationBuilder.cs` | Not configurable |
| PKI install_trust | Hardcoded `false` | — | `Proxy/ProxyConfigurationBuilder.cs` | Trust store installation disabled |
| SSE flush_interval | Hardcoded `-1` (flush immediately) | — | `Proxy/ProxyConfigurationBuilder.cs` | Required for SSE through the proxy |

---

### 5. Supervisor / Process Management

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| Shutdown timeout (per-process) | Capability `process.shutdownTimeoutSeconds` (via `CapabilityStore`) | `10` seconds (hardcoded fallback in supervisor) | `Supervisor/ProcessSupervisor.cs:StopProcessWithShutdownPolicyAsync` | Type binding defaults vary: dotnet-app=30s, nodejs-app=15s, system-service/executable=10s |
| Startup grace period (per-process) | Capability `process.startupGracePeriodSeconds` (via `CapabilityStore`) | `3` seconds | `ProcessConfiguration.cs` property default | Time process must survive before promoted to Running |
| Max startup retries (per-process) | Capability `process.maxStartupRetries` (via `CapabilityStore`) | `3` | `ProcessConfiguration.cs` property default + code fallback in supervisor | Fallback: `var maxStartupRetries = 3;` in `OnStartupFailure` if cap resolve fails |
| Restart policy (per-process) | Capability `restart.policy` (via `CapabilityStore`) | `RestartPolicy.Never` | `ProcessConfiguration.cs`, `ProcessSupervisor.cs` | Defaults vary by type: dotnet/node/system-service/executable = `OnCrash`; static-site has no restart |
| Success exit codes (per-process) | Capability `restart.successExitCodes` (via `CapabilityStore`) | `[0]` | `RestartConfiguration.cs`, `ProcessSupervisor.cs` | Hardcoded fallback `new[] { 0 }` if resolve fails |
| Max crash restarts before Fatal | Hardcoded `10` | — | `ManagedProcess.HasMaxRestartsExceeded(int maxRestarts = 10)` called with no argument | Not configurable |
| Restart backoff formula | Hardcoded exponential: `min(2^(failures-1), 60)` seconds | — | `ManagedProcess.GetBackoffDelay()` | Not configurable |
| Startup retry delay formula | Hardcoded linear: `StartupFailures` seconds | — | `ManagedProcess.GetStartupRetryDelay()` | Not configurable |
| Restart count reset threshold | Hardcoded `300` seconds (5 min) of healthy uptime | — | `ManagedProcess.ShouldResetRestartCount()` | Not configurable |
| Grace period check interval | Hardcoded `TimeSpan.FromSeconds(60)` | — | `ProcessSupervisor.cs` → `Timer` | Timer fires every 60s to reconcile Running processes |
| Log buffer capacity (per-process) | Hardcoded `1000` entries | — | `ProcessSupervisor.GetOrCreateLogBuffer` → `new RingBuffer<LogEntry>(1000)` | Not configurable |
| Log SSE max concurrent streams | Hardcoded `const int _maxConcurrentStreams = 10` | — | `Supervisor/LogStreamEndpoints.cs` | Not configurable |
| SSE keepalive interval | Hardcoded `TimeSpan.FromSeconds(30)` | — | `Supervisor/LogStreamEndpoints.cs` | Not configurable |
| SSE history burst size | Hardcoded `200` entries | — | `LogStreamEndpoints.cs:buffer.GetLastWithIds(200)` | Not configurable |
| Port allocation | OS bind-to-zero (`TcpListener(IPAddress.Loopback, 0)`) | Ephemeral | `Supervisor/PortAllocator.cs` | No range configuration; pure OS assignment |
| App stop/delete timeout | Hardcoded `TimeSpan.FromSeconds(10)` | — | `Registry/AppEndpoints.cs`, `Mcp/RegistrationTools.cs` | Stop-before-delete timeout; not configurable |
| Port injection env var name | Capability `port-injection.environmentVariableName` | `"PORT"` (executable/nodejs), `"ASPNETCORE_URLS"` (dotnet) | `PortInjectionConfiguration.cs` | Per-type binding defaults |
| Port injection format | Capability `port-injection.portFormat` | `"{port}"` (executable/nodejs), `"http://localhost:{port}"` (dotnet) | `PortInjectionConfiguration.cs` | Per-type binding defaults |
| Artifact location | Capability `artifact.location` | `""` (must be set) | `ArtifactConfiguration.cs` | Required; startup fails if empty or directory not found |
| Discovery strategy | Capability `process.discoveryStrategy` | `Manual` | `ProcessConfiguration.cs` | Per-type: dotnet=`DotNetRuntimeConfiguration`, nodejs=`PackageJson`, others=`Manual` |
| Environment variable defaults | Capability `environment-defaults.variables` | Type-dependent (see built-in JSONs) | `EnvironmentConfiguration.cs` | dotnet-app sets `ASPNETCORE_ENVIRONMENT=Production`, `NODE_ENV=production` for nodejs-app |

---

### 6. Aspire AppHost

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| API project name in Aspire | Hardcoded `"api"` | — | `AppHost/Program.cs` | Resource name for service discovery |
| API HTTP endpoint port | Hardcoded `.WithEndpoint("http", e => e.Port = 58400)` | 58400 | `AppHost/Program.cs` | Pinned; matches standalone `http` profile |
| Frontend project path | Hardcoded `"../../frontend"` | — | `AppHost/Program.cs` | Relative path from AppHost project root |
| API health check path | Hardcoded `"/health"` | — | `AppHost/Program.cs → .WithHttpHealthCheck` | Used by Aspire for dependency wait |
| Aspire → Frontend service injection | Aspire `WithReference(api)` injects `services__api__http__0` | — | `AppHost/Program.cs`, `vite.config.ts` | Vite reads this via `process.env.services__api__http__0` |
| Aspire DOTNET_ENVIRONMENT | `launchSettings.json` (AppHost) | `"Development"` | Inherited by child processes via Aspire | Not set in Api launchSettings (only AppHost) |

---

### 7. Capability System

The capability system is a two-tier settings mechanism for per-app behavioral configuration. It is not a single "setting" but an entire subsystem. Key architectural facts for the audit:

**Mechanism:**
- `CapabilityCatalog` — static frozen dictionary of 8 capability definitions (`process`, `port-injection`, `routing`, `health-check`, `environment-defaults`, `restart`, `auto-start`, `artifact`). Code-only, no DB table.
- **Tier 1 — Type Bindings:** JSON blobs per capability per app type, stored as embedded resources in `Data/BuiltInTypes/*.json` (5 built-in types) or as files in `UserTypes/` directory (user-defined types, hot-reloaded). Loaded by `TypeStore` at startup. These are the _defaults_ for all apps of that type.
- **Tier 2 — App Overrides:** `CapabilityOverride` entities in the SQLite DB, keyed by `(appId, capabilitySlug)`. Stored as JSON. Written by the settings UI/API or by `ProxyAppSeeder` at first boot.
- **Resolution:** `CapabilityResolver.Resolve<T>(bindingJson, overrideJson)` — pure static JSON merge, no I/O. Override fields win; missing override fields fall back to binding.
- **Class defaults:** Each `*Configuration` class has C# property defaults that activate when a field is absent from _both_ the binding and the override (third fallback tier).
- **Token substitution:** `{baseDomain}` tokens in type JSON are substituted with `Proxy:BaseDomain` at `TypeStore.LoadAsync` time, making the proxy base domain effectively baked into type binding defaults.

The built-in type defaults are documented in rows of Section 5 above. Per-app override values are user data (out of scope per the brief).

---

### 8. Logging / Telemetry

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| Default log level | `appsettings.json("Logging:LogLevel:Default")` | `"Information"` | ASP.NET Core logging pipeline | Standard .NET config key |
| ASP.NET Core log level | `appsettings.json("Logging:LogLevel:Microsoft.AspNetCore")` | `"Warning"` | ASP.NET Core logging pipeline | |
| OTLP exporter endpoint | env `OTEL_EXPORTER_OTLP_ENDPOINT` | not set (exporter disabled if empty) | `ServiceDefaults/Extensions.cs` | OTLP exporter only activated if this env var is non-empty |
| OTel include formatted message | Hardcoded `true` | — | `ServiceDefaults/Extensions.cs` | Not configurable |
| OTel include scopes | Hardcoded `true` | — | `ServiceDefaults/Extensions.cs` | Not configurable |
| OTel health/alive path filter | Hardcoded `/health` and `/alive` prefix exclusions | — | `ServiceDefaults/Extensions.cs` | Tracing skips health check requests |
| Aspire Dashboard OTLP URL | `launchSettings.json(AppHost) → DOTNET_DASHBOARD_OTLP_ENDPOINT_URL` | `http://localhost:19889` | Aspire dashboard | Aspire-specific; separate from the `OTEL_EXPORTER_OTLP_ENDPOINT` env var |
| Probe cache duration | Hardcoded `TimeSpan.FromMinutes(30)` | — | `Probes/ProbeService.cs` | Not configurable |
| AppStore cache duration | Hardcoded `TimeSpan.FromMinutes(5)` | — | `Registry/AppStore.cs` | Not configurable |
| UserStore cache duration | Hardcoded `TimeSpan.FromMinutes(5)` | — | `Authorization/UserStore.cs` | Not configurable |

---

### 9. Frontend

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| API base URL (Vite proxy target) | env `process.env.services__api__http__0` > hardcoded fallback | `"http://localhost:58400"` | `vite.config.ts` | Aspire injects the env var when running via AppHost; fallback is for standalone `npm run dev` |
| API base path (frontend) | Hardcoded string constant `"/api/v1"` | — | `frontend/src/lib/constants.ts` | All API calls prefixed with this |
| Auth storage key | Hardcoded `"collabhost-user-key"` | — | `frontend/src/lib/constants.ts` | localStorage key |
| Poll interval: app list | Hardcoded `3_000` ms | — | `frontend/src/lib/constants.ts` | TanStack Query refetch interval |
| Poll interval: app detail | Hardcoded `3_000` ms | — | `frontend/src/lib/constants.ts` | |
| Poll interval: dashboard | Hardcoded `3_000` ms | — | `frontend/src/lib/constants.ts` | |
| Poll interval: logs | Hardcoded `2_000` ms | — | `frontend/src/lib/constants.ts` | |
| Poll interval: routes | Hardcoded `10_000` ms | — | `frontend/src/lib/constants.ts` | |
| Poll interval: system | Hardcoded `30_000` ms | — | `frontend/src/lib/constants.ts` | |
| Poll interval: users | Hardcoded `30_000` ms | — | `frontend/src/lib/constants.ts` | |
| Frontend log buffer cap | Hardcoded `1_000` entries | — | `frontend/src/lib/constants.ts` | Client-side cap on log entries held in memory |
| Vite dev port | Hardcoded `5173` in `vite.config.ts` | — | Vite | Not configurable from env |
| `VITE_*` env vars | **None** | — | — | No `import.meta.env.*` references found anywhere in the frontend source |
| `.env` files | **None** | — | — | No `.env`, `.env.local`, etc. files exist in `frontend/` |

---

### 10. Other / Misc

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| MCP server transport: stateless | Hardcoded `options.Stateless = true` | — | `Mcp/_McpRegistration.cs` | Not configurable |
| MCP endpoint path | Hardcoded `"/mcp"` | — | `Mcp/_McpRegistration.cs` | |
| MCP auth skip prefix | Hardcoded `"/mcp"` in `AuthorizationMiddleware._skipPrefixes` | — | `Authorization/AuthorizationMiddleware.cs` | MCP handles its own auth via `McpAuthentication.ConfigureSessionAsync` |
| RingBuffer subscriber channel capacity | Hardcoded `256` per subscriber | — | `Shared/RingBuffer.cs` | Bounded channel used for SSE subscriber delivery |
| TypeStore FSW debounce delay | Hardcoded `500` ms | — | `Data/AppTypes/TypeStore.cs` | Coalesces rapid file-system events on `UserTypes/` directory |
| Health check path | Hardcoded `"/health"` | — | `ServiceDefaults/Extensions.cs` | |
| Liveness check path | Hardcoded `"/alive"` | — | `ServiceDefaults/Extensions.cs` | |
| OpenAPI spec path | Hardcoded via `app.MapOpenApi()` (default `/openapi/v1.json`) | — | `Program.cs` | Only in Development |
| Static files + SPA fallback | Hardcoded `"index.html"` | — | `Program.cs` | |
| CORS policy | Hardcoded `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` | — | `Program.cs` | Development only |
| App type slugs (system identity) | Hardcoded strings `"dotnet-app"`, `"nodejs-app"`, `"static-site"`, `"executable"`, `"system-service"` | — | Throughout supervisor, proxy, type store | Built-in type slugs are identity constants, not settings |
| Proxy app slug | Hardcoded `"proxy"` | — | `Proxy/ProxyManager.cs`, `ProxyAppSeeder.cs` | Proxy app is identified by this fixed slug in the DB |

---

## Observations

### O1 — `Platform:ToolsDirectory` is a dead config key

`appsettings.json` declares `Platform.ToolsDirectory = "tools"`, but no C# code in the repository reads this key via `IConfiguration`, `IOptions<>`, or any other mechanism. Either it is vestigial from an earlier design, or there is a consumer outside the audited scope. Should be removed or wired up.

### O2 — Admin key has two independent null-fallback paths that can diverge

`Authorization/_Registration.cs` generates a ULID in `PostConfigure<AuthorizationSettings>` when `AdminKey` is null. Then `Authorization/UserSeedService.cs` _also_ contains a separate `Ulid.NewUlid()` call in `StartAsync` as a fallback if `_authorizationSettings.AdminKey` is still null. Under normal operation `PostConfigure` always runs first, so `SeedService` never reaches its own ULID generator — but the duplicate path is a latent inconsistency. If the generated key from `PostConfigure` is null (e.g., a future refactor makes the setter non-public), the seed service would generate a _different_ key and the in-memory resolver would not match the DB row.

### O3 — Hardcoded shutdown timeout (10s) duplicated in code and capability schema default

`ProcessSupervisor.StopProcessWithShutdownPolicyAsync` has a hardcoded fallback of `var shutdownTimeoutSeconds = 10;` used when the capability resolve fails. `ProcessConfiguration.ShutdownTimeoutSeconds` defaults to `10` in the C# class. But the built-in type bindings override this at type level (dotnet-app = 30s, nodejs-app = 15s). So there are three layers for the same value: class default → type binding → app override. The supervisor fallback bypasses the class default entirely (it reads from the capability, not from `new ProcessConfiguration()`).

### O4 — Max runtime restart limit (10) is not in capability schema

`ManagedProcess.HasMaxRestartsExceeded(int maxRestarts = 10)` is always called with no argument (hardcoded 10), meaning the max-restarts ceiling for the crash-restart loop is **not exposed in the settings UI or capability schema**. Operators cannot tune this per-app, despite the startup-retry limit (`MaxStartupRetries`) being configurable. Asymmetric.

### O5 — `Proxy:SelfPort` and `launchSettings.json` port are coupled but independently declared

`appsettings.json("Proxy:SelfPort")` = `58400`, and `launchSettings.json("applicationUrl")` = `http://localhost:58400`. These must match for Caddy's self-route to reach the API. There is no code that cross-validates them. If an operator changes one without the other, the `collabhost.collab.internal` self-route silently breaks.

### O6 — Aspire env injection vs. standalone config are structurally different for the frontend

In Aspire mode, the Vite proxy target is `process.env.services__api__http__0` (injected by Aspire's `WithReference(api)`). In standalone mode, it falls back to the hardcoded `'http://localhost:58400'`. There is no `.env` or `VITE_*` mechanism — so a developer running `npm run dev` without Aspire has no way to point the frontend at a non-default API URL without editing `vite.config.ts`. This is a developer-experience gap.

### O7 — Several load-time supervisory constants are not configurable and would benefit from it

The following values are hardcoded and "feel like" they should be operator-tunable, especially for resource-constrained or high-density deployments:
- Log buffer capacity: 1,000 per app
- Max concurrent SSE streams: 10
- SSE history burst: 200 entries
- SSE keepalive: 30s
- AppStore / UserStore cache TTL: 5 min
- Probe cache: 30 min
- Grace period check interval: 60s
- Backoff formula cap: 60s

### O8 — `appsettings.Local.json` is the only environment-specific override path for the API

The API uses `AddJsonFile("appsettings.Local.json", optional: true)` as the sole local override mechanism (no `appsettings.{env}.json` layering beyond what ASP.NET Core loads by default). This means `appsettings.Development.json` does not exist and is not used. Operators who want to override settings (e.g., a custom `Proxy:BinaryPath`) must know to create `appsettings.Local.json`, which is a non-obvious convention.

### O9 — `{baseDomain}` token substitution in type JSON is load-time, not runtime

`TypeStore.ApplyTokens` replaces `{baseDomain}` in all type JSON (including built-in embedded resources) at `LoadAsync` time. This means changing `Proxy:BaseDomain` after startup requires a full restart AND a TypeStore reload to take effect in type bindings. The token is not resolved lazily at capability-resolve time.

---

## Coverage Gaps

1. **`appsettings.Local.json`** — gitignored, not in worktree. Cannot audit actual operator values. Its schema is fully inferred from `appsettings.json`.

2. **User-defined type files (`UserTypes/*.json`)** — directory does not exist in this worktree. The mechanism is fully understood but no user type files exist to inspect.

3. **Aspire internals** — `AddViteApp`, `WithReference`, `WithExternalHttpEndpoints`, and the Aspire DCP service discovery injection were inferred from code and Aspire conventions. The exact env vars injected into child processes (beyond `services__api__http__0`) were not exhaustively verified from Aspire SDK source.

4. **`Directory.Build.props`** — confirmed it contains no runtime-influencing settings (build metadata and analyzer configuration only).

5. **Test project overrides** — `Collabhost.Api.Tests` and `Collabhost.AppHost.Tests` use `WebApplicationFactory` and likely override settings in test fixtures. These are not platform settings and were not audited.

6. **MCP tool RBAC entitlements** — `Authorization/Entitlements.cs` likely contains hardcoded role-to-tool mappings. Not read as a settings file but may be relevant to auth architecture. Not audited.

7. **`nuget.config`** — not read at runtime; build infrastructure only.
