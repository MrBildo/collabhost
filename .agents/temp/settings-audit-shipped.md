# Settings Audit — Production API View

**Date:** 2026-04-18
**Branch:** `spec/153-release-pipeline`
**Source:** Filtered from `settings-audit.md` (full audit)
**Purpose:** Card #159 — settings-resolution architecture decision feed

This is the production-API view of the settings audit. Aspire orchestration, dev launch profiles, dev-only overrides, and frontend concerns have been excluded. Only settings that influence `Collabhost.Api` (and `Collabhost.ServiceDefaults`) at runtime in a standalone production deployment are included here. See `settings-audit.md` for the full picture.

---

## 1. Hosting / Process

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Operator-set env var | Not set in production (defaults to `"Production"`) | ASP.NET Core runtime | Controls: migration-on-startup, TypeStore.LoadAsync, ProxyAppSeeder, OpenAPI, CORS. **None of these run in Production** — all gated on `IsDevelopment()`. See Flag F1 below. |

> All other Hosting rows from the full audit (listen port, Aspire-injected ports, Aspire dashboard URLs, Vite dev port) are either `launchSettings.json`-only or AppHost-only and are dropped.

---

## 2. Data / Storage

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| SQLite connection string / DB path | `appsettings.json("ConnectionStrings:Host")` > hardcoded fallback | `"Data Source=./db/collabhost.db"` | `Data/_Registration.cs` → `configuration.GetConnectionString("Host")` | Fallback and appsettings value are identical; appsettings key is effectively a no-op unless operator overrides in `appsettings.Local.json` |
| User types directory | `appsettings.json("TypeStore:UserTypesDirectory")` > hardcoded fallback | `"UserTypes"` | `Data/AppTypes/_Registration.cs` → `TypeStoreSettings` | Resolved relative to `AppContext.BaseDirectory` unless rooted. See Flag F1 — TypeStore.LoadAsync is currently dev-only. |
| Tools directory | `appsettings.json("Platform:ToolsDirectory")` | `"tools"` | **Not read by any code** | Dead config key. No C# consumer found. See Observation O1 in the full audit. |

---

## 3. Auth

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| Admin key | `appsettings.json("Auth:AdminKey")` = `null` > `appsettings.Local.json` (operator override) | `null` | `Authorization/_Registration.cs` → `Configure<AuthorizationSettings>` | When null, a ULID is generated at startup and logged as WARNING. Ephemeral — regenerates on every restart. |
| Admin key (generated fallback) | `PostConfigure<AuthorizationSettings>` — generated ULID at startup | Runtime-generated ULID | `Authorization/_Registration.cs` | Logged to stdout. Use `appsettings.Local.json` to persist a stable key. |
| Admin DB seed key | Same resolved `IOptions<AuthorizationSettings>.AdminKey` | Same as above | `Authorization/UserSeedService.cs` | Seeds the first admin user row if DB is empty. Has its own null-fallback (see Observation O2 in full audit — latent dual-path inconsistency). |
| Auth skip paths | Hardcoded array `["/health", "/alive", "/openapi", "/mcp"]` | — | `Authorization/AuthorizationMiddleware.cs` | Not configurable |
| SSE query-param auth path | Hardcoded suffix check `"/logs/stream"` | — | `Authorization/AuthorizationMiddleware.cs` | Only path where `?key=` is accepted |
| `GET /status` bypass | Hardcoded (GET + exact path match) | — | `Authorization/AuthorizationMiddleware.cs` | Public endpoint, no key required |

> Dropped: frontend `"collabhost-user-key"` localStorage constant (frontend-only).

---

## 4. Proxy

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| Base domain | `appsettings.json("Proxy:BaseDomain")` | `"collab.internal"` | `Proxy/_Registration.cs` → `ProxySettings` | `required` keyword — throws if section missing |
| Caddy binary path | `appsettings.json("Proxy:BinaryPath")` | `"caddy"` | `Proxy/_Registration.cs` → `ProxySettings` | Bare name = PATH resolution; absolute/relative path also accepted |
| Caddy listen address | `appsettings.json("Proxy:ListenAddress")` | `":443"` | `Proxy/ProxyConfigurationBuilder.cs` | Caddy `servers.srv0.listen` value |
| TLS cert lifetime | `appsettings.json("Proxy:CertLifetime")` | `"168h"` | `Proxy/ProxyConfigurationBuilder.cs` | Caddy internal CA cert TTL |
| Self port (Collabhost → Caddy self-route) | `appsettings.json("Proxy:SelfPort")` | `58400` | `Proxy/ProxyConfigurationBuilder.cs` | Port Caddy reverse-proxies back to the API. **Must match the actual listen port.** No cross-validation. See Observation O5 in full audit. |
| Caddy admin API port | OS bind-to-zero (`PortAllocator.AllocatePort()`) at startup | Ephemeral OS-assigned | `Proxy/_Registration.cs` | Allocated fresh each boot; written into a temp bootstrap JSON. Not persisted. **Aspire-only at present** — standalone production deployment relies on the same mechanism but has no config override path. |
| Caddy admin base URL | Constructed from `AdminPort` at startup | `http://localhost:{ephemeral}` | `Proxy/_Registration.cs` → `HttpClient<CaddyClient>` | Scheme and host are hardcoded; only the ephemeral port varies |
| Proxy admin bootstrap config path | `Path.GetTempPath()` + `"collabhost/caddy-bootstrap.json"` | System temp dir | `Proxy/ProxyArgumentProvider.cs` | Not configurable |
| Route domain template | `{slug}.{baseDomain}` — resolved from type JSON + `ProxySettings.BaseDomain` | `"{slug}.{baseDomain}"` per built-in type JSONs | `Proxy/ProxyConfigurationBuilder.cs`, `Mcp/ConfigurationTools.cs` | `{baseDomain}` token substituted at TypeStore load time (load-time, not runtime — see Observation O9 in full audit) |
| Route sync startup delay | Hardcoded `TimeSpan.FromSeconds(2)` | 2 seconds | `Proxy/ProxyManager.cs` | Delay after proxy process Running before syncing routes. Not configurable. |
| PKI CA name | Hardcoded `"Collabhost Local Authority"` | — | `Proxy/ProxyConfigurationBuilder.cs` | Not configurable |
| PKI `install_trust` | Hardcoded `false` | — | `Proxy/ProxyConfigurationBuilder.cs` | Trust store installation disabled |
| SSE flush_interval | Hardcoded `-1` (flush immediately) | — | `Proxy/ProxyConfigurationBuilder.cs` | Required for SSE through the proxy |

---

## 5. Supervisor / Process Management

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| Shutdown timeout (per-process) | Capability `process.shutdownTimeoutSeconds` (via `CapabilityStore`) | `10`s hardcoded fallback in supervisor; type bindings vary (dotnet-app=30s, nodejs-app=15s, system-service/executable=10s) | `Supervisor/ProcessSupervisor.cs:StopProcessWithShutdownPolicyAsync` | Three-layer: class default → type binding → app override |
| Startup grace period (per-process) | Capability `process.startupGracePeriodSeconds` (via `CapabilityStore`) | `3`s | `ProcessConfiguration.cs` property default | Time process must survive before promoted to Running |
| Max startup retries (per-process) | Capability `process.maxStartupRetries` (via `CapabilityStore`) | `3` | `ProcessConfiguration.cs` property default + code fallback in supervisor | Fallback `var maxStartupRetries = 3;` in `OnStartupFailure` if cap resolve fails |
| Restart policy (per-process) | Capability `restart.policy` (via `CapabilityStore`) | `RestartPolicy.Never` (class default); type bindings: `OnCrash` for dotnet/node/system-service/executable | `ProcessConfiguration.cs`, `ProcessSupervisor.cs` | |
| Success exit codes (per-process) | Capability `restart.successExitCodes` (via `CapabilityStore`) | `[0]` | `RestartConfiguration.cs`, `ProcessSupervisor.cs` | Hardcoded fallback `new[] { 0 }` if resolve fails |
| Max crash restarts before Fatal | Hardcoded `10` | — | `ManagedProcess.HasMaxRestartsExceeded(int maxRestarts = 10)` — always called with no argument | Not configurable; not in capability schema (asymmetric vs. startup retry limit). See Observation O4 in full audit. |
| Restart backoff formula | Hardcoded exponential: `min(2^(failures-1), 60)` seconds | — | `ManagedProcess.GetBackoffDelay()` | Not configurable |
| Startup retry delay formula | Hardcoded linear: `StartupFailures` seconds | — | `ManagedProcess.GetStartupRetryDelay()` | Not configurable |
| Restart count reset threshold | Hardcoded `300`s (5 min) of healthy uptime | — | `ManagedProcess.ShouldResetRestartCount()` | Not configurable |
| Grace period check interval | Hardcoded `TimeSpan.FromSeconds(60)` | — | `ProcessSupervisor.cs` → `Timer` | Timer fires every 60s to reconcile Running processes |
| Log buffer capacity (per-process) | Hardcoded `1000` entries | — | `ProcessSupervisor.GetOrCreateLogBuffer` → `new RingBuffer<LogEntry>(1000)` | Not configurable |
| Log SSE max concurrent streams | Hardcoded `const int _maxConcurrentStreams = 10` | — | `Supervisor/LogStreamEndpoints.cs` | Not configurable |
| SSE keepalive interval | Hardcoded `TimeSpan.FromSeconds(30)` | — | `Supervisor/LogStreamEndpoints.cs` | Not configurable |
| SSE history burst size | Hardcoded `200` entries | — | `LogStreamEndpoints.cs:buffer.GetLastWithIds(200)` | Not configurable |
| Port allocation | OS bind-to-zero (`TcpListener(IPAddress.Loopback, 0)`) | Ephemeral | `Supervisor/PortAllocator.cs` | No range configuration |
| App stop/delete timeout | Hardcoded `TimeSpan.FromSeconds(10)` | — | `Registry/AppEndpoints.cs`, `Mcp/RegistrationTools.cs` | Stop-before-delete; not configurable |
| Port injection env var name | Capability `port-injection.environmentVariableName` | `"PORT"` (executable/nodejs), `"ASPNETCORE_URLS"` (dotnet) | `PortInjectionConfiguration.cs` | Per-type binding defaults |
| Port injection format | Capability `port-injection.portFormat` | `"{port}"` (executable/nodejs), `"http://localhost:{port}"` (dotnet) | `PortInjectionConfiguration.cs` | Per-type binding defaults |
| Artifact location | Capability `artifact.location` | `""` (must be set) | `ArtifactConfiguration.cs` | Required; startup fails if empty or directory not found |
| Discovery strategy | Capability `process.discoveryStrategy` | `Manual` (class default); dotnet=`DotNetRuntimeConfiguration`, nodejs=`PackageJson` | `ProcessConfiguration.cs` | Per-type binding defaults |
| Environment variable defaults | Capability `environment-defaults.variables` | Type-dependent (dotnet-app sets `ASPNETCORE_ENVIRONMENT=Production`; nodejs-app sets `NODE_ENV=production`) | `EnvironmentConfiguration.cs` | Built-in type JSONs |
| Auto-start | Capability `auto-start.enabled` (via `CapabilityStore`) | Type-dependent | `ProcessSupervisor.StartAsync` | Evaluated at supervisor hosted service startup |
| RingBuffer subscriber channel capacity | Hardcoded `256` per subscriber | — | `Shared/RingBuffer.cs` | Bounded channel for SSE subscriber delivery |

---

## 6. Capability System

The capability system is a two-tier settings mechanism for per-app behavioral configuration. It is not a single "setting" but an entire subsystem.

**Mechanism:**
- `CapabilityCatalog` — static frozen dictionary of 8 capability definitions (`process`, `port-injection`, `routing`, `health-check`, `environment-defaults`, `restart`, `auto-start`, `artifact`). Code-only, no DB table.
- **Tier 1 — Type Bindings:** JSON blobs per capability per app type, stored as embedded resources in `Data/BuiltInTypes/*.json` (5 built-in types) or as files in `UserTypes/` directory (user-defined types). Loaded by `TypeStore` at startup (see Flag F1).
- **Tier 2 — App Overrides:** `CapabilityOverride` entities in the SQLite DB, keyed by `(appId, capabilitySlug)`. Written by the settings UI/API.
- **Resolution:** `CapabilityResolver.Resolve<T>(bindingJson, overrideJson)` — pure static JSON merge, no I/O.
- **Class defaults:** C# property defaults activate when a field is absent from both binding and override.
- **Token substitution:** `{baseDomain}` tokens in type JSON are substituted at `TypeStore.LoadAsync` time — load-time bake, not lazy runtime resolution.

The capability defaults are documented in the Supervisor rows above. Per-app override values are user data (out of scope).

---

## 7. Logging / Telemetry

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| Default log level | `appsettings.json("Logging:LogLevel:Default")` | `"Information"` | ASP.NET Core logging pipeline | Standard .NET config key |
| ASP.NET Core log level | `appsettings.json("Logging:LogLevel:Microsoft.AspNetCore")` | `"Warning"` | ASP.NET Core logging pipeline | |
| OTLP exporter endpoint | env `OTEL_EXPORTER_OTLP_ENDPOINT` | Not set (exporter disabled if empty) | `ServiceDefaults/Extensions.cs` | Real production knob. OTLP export only activates if this env var is non-empty. Not Aspire-specific — any OTLP collector endpoint works. |
| OTel include formatted message | Hardcoded `true` | — | `ServiceDefaults/Extensions.cs` | Not configurable |
| OTel include scopes | Hardcoded `true` | — | `ServiceDefaults/Extensions.cs` | Not configurable |
| OTel health/alive path filter | Hardcoded `/health` and `/alive` prefix exclusions | — | `ServiceDefaults/Extensions.cs` | Tracing skips health check requests |
| AppStore cache duration | Hardcoded `TimeSpan.FromMinutes(5)` | — | `Registry/AppStore.cs` | Not configurable |
| UserStore cache duration | Hardcoded `TimeSpan.FromMinutes(5)` | — | `Authorization/UserStore.cs` | Not configurable |
| Probe cache duration | Hardcoded `TimeSpan.FromMinutes(30)` | — | `Probes/ProbeService.cs` | Not configurable |
| TypeStore FSW debounce delay | Hardcoded `500`ms | — | `Data/AppTypes/TypeStore.cs` | Coalesces rapid filesystem events on `UserTypes/` directory |

> Dropped: `DOTNET_DASHBOARD_OTLP_ENDPOINT_URL` from `launchSettings.json` — Aspire dashboard only. `OTEL_EXPORTER_OTLP_ENDPOINT` is the real production OTLP knob.

---

## 8. Misc

| Setting (logical name) | Source(s) today | Default (if any) | Read site(s) | Notes |
|---|---|---|---|---|
| MCP server transport: stateless | Hardcoded `options.Stateless = true` | — | `Mcp/_McpRegistration.cs` | Not configurable |
| MCP endpoint path | Hardcoded `"/mcp"` | — | `Mcp/_McpRegistration.cs` | |
| MCP auth skip prefix | Hardcoded `"/mcp"` | — | `Authorization/AuthorizationMiddleware.cs` | MCP handles its own auth |
| Health check path | Hardcoded `"/health"` | — | `ServiceDefaults/Extensions.cs` | |
| Liveness check path | Hardcoded `"/alive"` | — | `ServiceDefaults/Extensions.cs` | |
| OpenAPI spec path | Default `app.MapOpenApi()` (`/openapi/v1.json`) | — | `Program.cs` | **Dev-only** — only mapped inside `IsDevelopment()` block |
| SPA fallback | Hardcoded `"index.html"` | — | `Program.cs` | Static files + SPA fallback always active |
| CORS policy | Hardcoded `AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader()` | — | `Program.cs` | **Dev-only** — only registered inside `IsDevelopment()` block |
| App type slugs | Hardcoded strings `"dotnet-app"`, `"nodejs-app"`, `"static-site"`, `"executable"`, `"system-service"` | — | Throughout supervisor, proxy, type store | Identity constants, not configurable settings |
| Proxy app slug | Hardcoded `"proxy"` | — | `Proxy/ProxyManager.cs`, `ProxyAppSeeder.cs` | Fixed identity slug in DB |
| `appsettings.Local.json` presence | Optional JSON file, not committed | — | `Program.cs` → `AddJsonFile(..., optional: true)` | Sole local-override path for production operators (no `appsettings.Production.json`). Must be created manually. |

---

## Flags Surfaced During Filter

### F1 — TypeStore.LoadAsync and ProxyAppSeeder.SeedAsync are dev-only (potential production gap)

In `Program.cs`, both `typeStore.LoadAsync()` and `proxySeeder.SeedAsync()` are called inside `if (app.Environment.IsDevelopment())`. This means in a production deployment (`ASPNETCORE_ENVIRONMENT` not set to `"Development"`):

- The TypeStore is **never loaded** at startup
- Built-in type bindings and user types are **never read**
- The proxy app is **never seeded** into the DB on first boot
- The capability system's Tier 1 (type bindings) is unavailable

Yet `ProcessSupervisor` and `ProxyManager` both receive `TypeStore` as a constructor dependency and call it at runtime (`_typeStore.HasBinding(...)`, `_typeStore.GetSnapshot()`). If TypeStore was never loaded, those calls operate on an empty/initial snapshot.

This is either:
(a) A deliberate production gap (Collabhost is currently dev-only), or
(b) A production startup bug that will manifest when the first production deployment is attempted.

The full audit does not flag this. It warrants a card or explicit clarification before the release pipeline work proceeds.

---

## What Changed from the Full Audit

The following were dropped from the full `settings-audit.md`:

- **Section 6 — Aspire AppHost** (entire section): All rows are AppHost orchestration wiring, parameters, pinned ports, and project references. None of these exist in a standalone `Collabhost.Api` production deployment.
- **Section 1 (Hosting) — All rows except `ASPNETCORE_ENVIRONMENT`**: `launchSettings.json` HTTP/HTTPS listen ports, `ASPIRE_ALLOW_UNSECURED_TRANSPORT`, `DOTNET_ASPIRE_SHOW_DASHBOARD_RESOURCES`, Aspire DCP/dashboard URLs — all dev or Aspire-runtime-only.
- **Section 9 — Frontend** (entire section): Vite config, `process.env.services__api__http__0`, poll intervals, `VITE_*` vars, `.env` files — all frontend-only or Vite dev server config.
- **Section 3 (Auth) — `"collabhost-user-key"` localStorage constant**: Frontend-only.
- **Section 8 (Logging) — `DOTNET_DASHBOARD_OTLP_ENDPOINT_URL`**: Aspire dashboard OTLP URL; not a production API setting. `OTEL_EXPORTER_OTLP_ENDPOINT` (a real production knob) was kept.
- **Section 10 (Misc) — OpenAPI, CORS**: Retained but annotated as dev-only (gated by `IsDevelopment()`).

**Judgment calls:**
- `ASPNETCORE_ENVIRONMENT` was kept despite being a "dev convenience" setting in the original audit — it is the primary gate for multiple production-relevant startup behaviors and operators must know to set (or not set) it.
- `Caddy admin API port` (ephemeral, Aspire-injected in dev) was kept and marked "Aspire-only at present" because the standalone production deployment uses the same ephemeral mechanism with no config override path — the team needs to know.
- `OTEL_EXPORTER_OTLP_ENDPOINT` was kept: it reads from `builder.Configuration` (which includes env vars), not from Aspire injection, making it a genuine production telemetry knob.
- Hardcoded operational thresholds (backoff, restart limits, cache TTLs, buffer sizes) were kept in full — they are production-relevant tuning surfaces even if not currently configurable.
