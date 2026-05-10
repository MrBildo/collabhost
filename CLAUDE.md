# Collabhost — Self-Hosted Application Platform

## Identity

Collabhost is a **mini self-hosted application platform** — a control plane for managing local services, workers, MCP servers, and scheduled jobs from a single dashboard. It is not a full PaaS. It manages processes on the host machine, routes traffic through Caddy, and provides operational visibility.

## Repository Rules — Hard

These rules are non-negotiable and apply to every agent, every dispatch, every commit.

1. **`.agents/*` and `.claude/*` are permanently gitignored. Local-only. No exceptions.**
   - These directories are agent workspaces, review artifacts, dispatch logs, specs, and harness state. They are private to each operator's machine.
   - **Never `git add --force` a path under `.agents/*` or `.claude/*`.** Not for "preserving review files." Not for "consistency with prior tracked files." Not for any reason.
   - If a reviewer or planner produces an artifact (review doc, diagnosis, brief, spec) inside a worktree's `.agents/temp/`, **copy it OUT** to the main repo's (also gitignored) `.agents/temp/` before cleaning up the worktree. The file persists locally, never enters git.
   - If you find tracked files under these paths, scrub via `git rm --cached -r .agents .claude` on a chore branch and PR to main.

2. **Specs live under `.agents/specs/` and are NOT part of the published source.** Source code comments must not reference spec documents (e.g., `// per .agents/specs/release-pipeline.md §6.2`). Cross-reference internal design via card numbers, GitHub issues, or inline rationale — anything an external reader of the published source can resolve.

3. **Production posture: design for backward compatibility.**
   - Collabhost is at v1.0.0 (sunset of pre-production posture, 2026-05-01). Operators installing v1.0.0 are no longer "well aware of the possibility of breaking changes" — durable homelab deployments are now real, and the cost of a breaking change is operator-facing.
   - **Surface migration concerns and design for them.** This applies to settings schema, API shape, persisted state, and operator-facing contracts. New settings keys, API additions, and schema changes must consider how an existing v1.0.x install reaches the new shape without operator intervention.
   - When a breaking change is genuinely required, the operator must be told: an explicit migration step in the installer or release notes, a deprecation window where both shapes work, or a tool that performs the transformation. Silent breakage is not an option.
   - **Historical reference — pre-production posture (in force from project start through v0.1.x):** *"We are still in a pre-production state. Anyone currently using Collabhost is well aware of the possibility of breaking changes. So regarding setting migration concerns, they are nil. I want everyone to be clear, surface migration concerns, but don't plan around them, prioritize, scope, or design for them unless I tell you to. At some point we will have those concerns. That is not today or tomorrow."* — Bill Wheelock, 2026-04-29. The "at some point" arrived at the v1.0.0 tag.

## Coding Conventions

### Skills agents must use

- **C# / .NET work:** invoke the `dotnet-dev` skill (code style + tooling) AND `do-dotnet-backend-architecture` (subsystem shape, domain modeling, EF Core, API contracts).
- **TypeScript / React work:** invoke the `typescript-dev` skill.

The skills carry the universal patterns. The sections below name only Collabhost-specific overrides.

### .NET overrides

#### `App` is the product entity name (naming exception)

The domain entity `App` keeps its short form despite the `dotnet-dev` no-abbreviations rule. `App` is the product's named concept (`AppType`, `AppStore`, `Apps/` feature folder). `Application` is reserved for ASP.NET / hosting concepts.

#### Authorization

- Header-based: `X-User-Key` (ULID).
- Two role types: `Administrator`, `Agent`.
- No ASP.NET authentication middleware — auth check is custom logic invoked early in the pipeline.
- Admin key auto-generated on first run if not configured (`Auth:AdminKey` setting).
- Endpoints that need to return 403 use `Results.StatusCode(403)` directly — `TypedResults.Forbid()` returns a `ForbidHttpResult` that wants a registered authentication scheme and will throw at runtime.

#### Local analyzer suppressions

Documented inline in `.editorconfig`, `backend/Directory.Build.props`, and `backend/Directory.Build.targets`. Full list of project-tuned suppressions:

**`.editorconfig` (severity = none):**
- `CA1708`, `MA0038`, `MA0041`, `CA1822` — false positives on C# 14 extension blocks (will suppress the same way on every project using extension blocks).
- `MA0003` — single internal-only enum, no display strings worth localizing.
- `MA0007` — trailing commas not enforced (style preference).
- `MA0018` — false positive on `CommandResult<T>.Success()` shape.
- `MA0176` — false positive on catalog Guid constants and EF migration ID literals.

**`backend/Directory.Build.props` `<NoWarn>`:**
- `CA1873`, `CS1591`, `S3881`, `S6966` — see inline rationale comments in the file.

**`backend/Directory.Build.targets` (test projects only):**
- `CA1707` — test method names use `MethodName_Scenario_ExpectedResult` with underscores.
- `VSTHRD200` — xUnit `[Fact]`/`[Theory]` methods conventionally omit the `Async` suffix; community norm predates the analyzer. Project-wide suppression avoids per-class `#pragma` noise.

### Frontend overrides

#### Styling

Tailwind CSS v4 with `@tailwindcss/vite` (NOT PostCSS). Design system is **War Machine** — visual identity lives in CSS custom properties under `src/styles/tokens.css` (`--wm-*` tokens, amber primary), surfaced as component classes (`wm-*`) in `components.css`. Tailwind handles layout (`flex`, `grid`, `gap-*`); `wm-*` classes handle visual identity. Typography is **IBM Plex Mono / Plex Sans**. The app is dark-mode only — no theme switcher. The `cn()` helper in `lib/cn.ts` is plain `clsx` — there is no `tailwind-merge`.

#### API client

`src/api/client.ts` is a thin `fetch` wrapper that injects `X-User-Key` from `localStorage` (key `AUTH_STORAGE_KEY`). On 401, the wrapper clears the key and emits an auth-change event so `<AuthGate>` re-mounts. Use the wrapper through typed endpoint functions in `src/api/endpoints.ts` — never call `fetch` directly from a hook or component.

#### Polling

Use `POLL_INTERVALS` from `lib/constants.ts` as the `refetchInterval` source — never bare-number intervals in queries.

#### Vendor abstraction

The reverse proxy is exposed in the UI as "Proxy" — never "Caddy". Backend identifiers and types use the abstract name (`proxyState`, `useReloadProxy`, etc.). If a status string, error message, or button label leaks "Caddy", it's a bug. Backend internals may name Caddy directly; the moment a value crosses into the frontend or into a user-facing log line, it's "Proxy".

### Cross-tier discipline

**No frontend hacks for backend concerns.** If a frontend agent encounters something that should be fixed on the backend (wrong datetime format, missing endpoint, hardcoded lookup data), report the gap rather than translate. Backend gaps get fixed on the backend, not papered over with frontend translation layers.

## Mental Model

- **Caddy** = front door (edge reverse proxy, TLS, routing)
- **ASP.NET Core** = control tower (app registry, process supervision, auth)
- **React dashboard** = operator console (War Machine design system)
- **SQLite** = persistence (zero-config)

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10 / C# (Minimal API, EF Core, SQLite) |
| Frontend | React 19 + TypeScript, Vite, Biome, War Machine design system |
| Testing | xUnit + Shouldly (backend), Vitest (frontend) |
| Orchestration | Aspire 13.3, OpenTelemetry |
| Reverse Proxy | Caddy (edge), managed by Collabhost via JSON API |

## Active Branch

Development is on `main`. The v1 codebase is preserved on `v0-reference` for reference only.

## Structure

```
collabhost/
├── CLAUDE.md                  # This file
├── .editorconfig              # Code style (tool-enforced)
├── nuget.config               # NuGet feed config (Aspire preview)
├── .agents/                   # Instance-local workspace (gitignored)
│   ├── specs/                 # Architecture specs and feature specs
│   ├── agents/{name}/         # Named agent workspaces (journal, TODO, archive)
│   ├── research/
│   └── temp/                  # Design discussions, roundtable artifacts
├── backend/
│   ├── Collabhost.slnx
│   ├── Directory.Build.props  # Shared build config (analyzers, doc gen, NoWarn)
│   ├── Directory.Build.targets # Test-project conditional NoWarn
│   ├── Collabhost.AppHost/    # Aspire orchestrator
│   ├── Collabhost.ServiceDefaults/  # Shared telemetry/health
│   ├── Collabhost.Api/        # Main API project
│   │   ├── ActivityLog/       # ActivityEvent store + query endpoint (operator-visible audit)
│   │   ├── Authorization/     # Auth middleware (X-User-Key header, ?key= for SSE only), Users, roles
│   │   ├── Capabilities/      # Catalog, config types, schemas, resolver, override store
│   │   ├── Dashboard/         # Dashboard stats + events endpoints
│   │   ├── Data/              # EF Core DbContext, migrations, AppType file-store, built-in type JSON
│   │   ├── Events/            # Generic typed event bus
│   │   ├── Filesystem/        # Filesystem browse + strategy detection endpoints
│   │   ├── Mcp/               # MCP server (tools, auth, server instructions) at /mcp
│   │   ├── Platform/          # Boot version tracking, version info, startup preflight, self-port validator
│   │   ├── Probes/            # Project metadata extractors (dotnet, node, react, typescript) + curator
│   │   ├── Proxy/             # ProxyManager, CaddyClient, config builder, seeder
│   │   ├── Registry/          # App entity, AppStore, CRUD + lifecycle endpoints
│   │   ├── Shared/            # RingBuffer, LogEntry, PathExtensions, UTC date converter
│   │   ├── Supervisor/        # ProcessSupervisor, ManagedProcess, PortAllocator, platform runners
│   │   │   └── Containment/   # IProcessContainment + Windows (Job Objects), Linux, Null impls
│   │   ├── System/            # Status + version endpoints (public, no auth)
│   │   ├── Updates/           # Update-check subsystem (placeholder; in flight)
│   │   └── Properties/        # launchSettings.json (required for Aspire)
│   ├── Collabhost.Api.Tests/  # Integration tests (WebApplicationFactory + fakes)
│   └── Collabhost.AppHost.Tests/  # Aspire smoke tests (real Kestrel + SQLite)
├── frontend/
│   ├── package.json
│   ├── biome.json             # Linting + formatting
│   ├── vite.config.ts
│   ├── vitest.config.ts
│   └── src/
│       ├── actions/           # ActionButton, ActionBar
│       ├── api/               # Client, endpoints, types
│       ├── chrome/            # Topbar, Layout, AuthGate, Breadcrumbs
│       ├── forms/             # SchemaField, RegistrationField, form inputs
│       ├── hooks/             # TanStack Query hooks per domain (incl. use-log-stream SSE hook)
│       ├── lib/               # cn helper, constants (POLL_INTERVALS), routes, format helpers
│       ├── log/               # LogViewer, LogLine
│       ├── pages/             # All page components
│       ├── probes/            # Probe panels (dotnet, node, react, typescript) + registry
│       ├── shared/            # EmptyState, ErrorBanner, ConfirmDialog, etc.
│       ├── status/            # StatusDot, StatusText, StatusStrip, StatsStrip
│       ├── styles/            # War Machine tokens, components, reset
│       ├── tables/            # DataTable, FilterBar, app-columns
│       └── test/              # Test setup + helpers for Vitest
└── tools/                     # Demo + scratch projects used during dev (gitignored fixtures, demo apps)
    └── generate-ids.cs        # ULID/GUID generator for seed data
```

## Build & Run

### Recommended: Aspire

```powershell
aspire start
```

This starts the API, frontend Vite dev server, and Aspire dashboard with OpenTelemetry. Use `aspire describe` to check resource status.

### Standalone

```powershell
# Backend only
dotnet run --project backend/Collabhost.Api

# Frontend only
cd frontend && npm run dev
```

### Linux (WSL2)

For Linux-native testing. See `ecosystem/docs/wsl2-linux-test-environment.md` for full setup.

**One-shot commands** (build, test) work via `wsl bash -c`:

```powershell
wsl bash -c "cd ~/projects/collab/collabhost/backend && dotnet test Collabhost.slnx"
```

**Long-running processes** (dev servers) require the tmux MCP tool — configured as a user-scoped MCP server (see `ecosystem/projects/dotnet/tmux-mcp/`):

- `tmux_create_session` — use absolute paths (`/home/<user>/projects/...`), not `~`
- `tmux_send_keys` — start the process (`npx vite --port 5173 --host`, `dotnet run`)
- `tmux_send_and_capture` — send a command and capture output in one call (preferred for quick checks)
- `tmux_wait_for_pattern` — block until output matches a regex (preferred over polling for builds/tests)
- `tmux_capture_pane` — one-shot output read, use bounded `lines` (10-200)
- `tmux_kill_session` — clean up when done

Aspire does NOT run workloads natively on Linux. Use standalone `dotnet run` for Linux testing.

### Tests & Verification

```powershell
# Backend (MUST use solution build with --no-incremental — see dotnet-dev skill for why)
cd backend && dotnet build Collabhost.slnx --no-incremental
cd backend && dotnet format Collabhost.slnx --verify-no-changes
cd backend && dotnet test                                    # Runs both Api.Tests and AppHost.Tests

# Frontend (Biome — not ESLint/Prettier)
cd frontend && npm run build                                 # tsc typecheck + Vite bundle
cd frontend && npm run lint                                  # Biome — 0 errors
cd frontend && npm run format:check                          # Biome — clean
cd frontend && npm run test                                  # Vitest — all green
```

## Core Subsystems

### App Registry (`Registry/`)
- `App` entity with ULID primary key, slug-based identity. Slug is the immutable external handle.
- `AppType` (separate subsystem under `Data/AppTypes/`) is a slug-keyed file-backed type record loaded by `TypeStore` from `Data/BuiltInTypes/*.json` plus an optional user-types directory.
- `AppStore` singleton with `IDbContextFactory<AppDbContext>` + `IMemoryCache`
- 5 built-in app types: `dotnet-app`, `nodejs-app`, `static-site`, `executable`, `system-service`
- Slug: unique, immutable, `[a-z0-9-]`, used in API routes and domain names

### Capability System (`Capabilities/`)
- 8 behavioral capabilities: process, port-injection, routing, health-check, environment-defaults, restart, auto-start, artifact
- Static `CapabilityCatalog` (code-only, no DB table) — `FrozenDictionary<string, CapabilityDefinition>` keyed by capability slug, defines display name + configuration type + schema.
- Type-level defaults live on the `AppType` JSON files (`Data/BuiltInTypes/*.json` and the user-types dir) as a `capabilities` dictionary. Per-app overrides are stored as `CapabilityOverride` rows (`AppId` + `CapabilitySlug` + `ConfigurationJson`).
- `CapabilityResolver.Resolve<T>(defaultJson, overrideJson)` — pure static function, deep JSON merge, no I/O. `ResolveJson(...)` returns the merged JSON without deserialization. `ValidateEdits(...)` enforces schema known-key + Locked/Derived field rules.
- Schema-driven: co-located `Schema` static property on each config type drives form generation and validation

### Process Supervisor (`Supervisor/`)
- Singleton `IHostedService`, `ConcurrentDictionary<Ulid, ManagedProcess>`
- Start/stop/restart/kill managed processes
- `IManagedProcessRunner` interface with platform-specific implementations: `WindowsProcessRunner`, `LinuxProcessRunner`, and `FallbackProcessRunner` (uses `System.Diagnostics.Process` where the native runners do not apply).
- `IProcessContainment` / `IContainmentHandle` — process containment abstraction. Implementations under `Supervisor/Containment/`: `WindowsJobObjectContainment` (Windows Job Objects), `LinuxContainment`, and `NullContainment` (no-op fallback).
- Windows: CreateProcess P/Invoke, process groups, `GenerateConsoleCtrlEvent` for graceful shutdown, Job Objects for orphan protection.
- Linux: native fork/exec via `LinuxNativeMethods` with cgroup v2 containment for orphan protection (see `.agents/specs/linux-process-management.md` for the design).
- Stdout/stderr capture into in-memory ring buffer
- Crash detection + restart with exponential backoff
- Discovery strategy via switch expression (DotNetRuntimeConfiguration, PackageJson, Manual)
- Port allocation via bind-to-zero (`PortAllocator`)
- Auto-start on Collabhost startup
- Static sites: start/stop toggles Caddy routing (no process)
- Log streaming: SSE endpoint registered in `LogStreamEndpoints` consumes the same ring buffer.

### Event Bus (`Events/`)
- Generic `IEventBus<T>` with in-memory implementation
- `ProcessStateChangedEvent` published on state transitions
- Proxy subsystem subscribes to sync routes on state changes

### Proxy Management (`Proxy/`)
- `ProxyManager` singleton `IHostedService`, subscribes to process state events
- Channel-based sequential processor for ordered route syncs
- `ProxyConfigurationBuilder.Build()` — pure function
- `ICaddyClient` interface + `CaddyClient` (HttpClientFactory)
- `ProxyAppSeeder` — seeds Caddy as a managed system-service app
- `@id` tags on every route for direct CRUD
- Route: `{slug}.collab.internal` → reverse_proxy or file_server
- HTTPS via Caddy internal CA + `tls internal`
- EnableRoute/DisableRoute for static site start/stop

### Dashboard (`Dashboard/`)
- `GET /dashboard/stats` — aggregated counts (total, running, stopped, crashed, backoff, fatal)
- `GET /dashboard/events` — recent activity-log events for the dashboard feed

### Activity Log (`ActivityLog/`)
- `ActivityEvent` rows persisted via EF Core, queryable through `GET /events`
- Subsystems publish events via `ActivityEventStore`; events surface in the dashboard feed and per-app views.

### Auth (`Authorization/`)
- Header-based: `X-User-Key` (ULID)
- Query param `?key=` fallback scoped to SSE endpoints (`/logs/stream`) only
- Middleware skips path prefixes: `/health`, `/alive`, `/openapi`, `/mcp` (MCP has its own auth filter)
- Public GETs (no key required): `GET /api/v1/status`, `GET /api/v1/version`
- `User` entity with `UserRole` (Administrator, HumanUser, AgentUser); `UserStore` and `UserSeedService` handle the admin-key 3-scenario boot model (blind first run generates + prints; configured first run seeds silently; subsequent boot with a new configured key inserts an additional break-glass admin).
- `RequireRoleFilter` is the endpoint-level guard for role-restricted routes (e.g. `/users/*`).
- Admin key from config (`Auth:AdminKey`)

### MCP (`Mcp/`)
- Streamable HTTP MCP server mounted at `/mcp` (separate from REST surface; bypasses the standard auth middleware and uses `McpAuthentication`).
- Tool surface: `RegistrationTools`, `LifecycleTools`, `ConfigurationTools`, `DiscoveryTools`, `ActivityLogTools`.
- `McpServerInstructions` carries the operator-facing tool guidance.

### Platform (`Platform/`)
- `VersionInfo.Current` — build-stamped version string surfaced via `/api/v1/version` and stderr-printed on startup.
- `BootVersionTracker` / `IBootVersionWriter` — records the last-booted version into `data/`. Drives migration's "from-version" detection.
- `StartupPreflight` — validates data + user-types directories before DI is built; failure halts before any DB touch (exit 10).
- `StartupStderr` — structured stderr writer for fatal startup failures (summary + details + recovery steps).
- `ListenPortValidator` — cross-checks `Hosting:ListenPort` config against Kestrel's bound listen port and warns on mismatch.

### Portal (`Portal/`)
- Static-asset shipping for the React dashboard. The dashboard ships in the same binary as the API and is served via `app.UseDefaultFiles()` + `app.UseStaticFiles()` + a custom `UsePortalSpaFallback()` middleware that handles React Router client-side routes. All three run in the middleware phase **before** auth, so the SPA shell, the dashboard root, and SPA deep links are reachable unauthenticated; auth is enforced at API-call time by `<AuthGate>` calling `/api/v1/auth/me`. The auth wall continues to gate `/api/v1/*`, `/health`, `/alive`, `/openapi`, and `/mcp` as today.
- `PortalSettings.Subdomain` — env `COLLABHOST_PORTAL_SUBDOMAIN` > `Portal:Subdomain` appsetting > hardcoded fallback `"collabhost"`. Resolved value is consumed by `ProxyConfigurationBuilder.BuildSelfRoute` and `BuildSubjectList` to set the Portal's proxy-fronted hostname. `RouteEntry.Domain` carries pre-resolved per-app hostnames (operators can customize via `RoutingConfiguration.DomainPattern`).
- `PortalReachabilityCheck` — soft preflight wired into `app.Lifetime.ApplicationStarted` alongside `ListenPortValidator`. Warns (does not halt) when `wwwroot/index.html` is missing or `wwwroot/assets/` is empty. Two legitimate "missing" states exist (packaging regression, intentional stripped deployment); halting boot would trade a degraded mode for a fully unreachable one.
- The Portal is **not** a registered app; it does not appear in `/api/v1/apps`, has no `AppType`, and is not seeded via `ProxyAppSeeder`. The `wwwroot/` directory is part of the archive contract (item 7 of 7). `/api/v1/routes` synthesizes a Portal row at index 0 (`isPortal: true`) so operators can see the resolved hostname; `/api/v1/status` carries `portalUrl`.

### Probes (`Probes/`)
- Project metadata extractors keyed off the app's artifact path. `DotnetExtractor`, `NodeExtractor`, `TypeScriptExtractor` (and a React panel layered on top of node) emit structured probe data consumed by the frontend's per-app detail view.
- `ProbeService` orchestrates extraction; `ProbeCurator` selects which probes apply per app type.
- `ProbeStartupService` warms the cache on boot.

### Filesystem (`Filesystem/`)
- `GET /filesystem/browse` — directory listing for the registration form's path picker.
- `GET /filesystem/detect-strategy` — sniffs an artifact path and returns the recommended discovery strategy (DotNetRuntimeConfiguration, PackageJson, Manual).

## API Surface

REST API under `/api/v1/`:

```
GET    /api/v1/apps                          # App list (AppListItem)
GET    /api/v1/apps/{slug}                   # App detail (AppDetail)
GET    /api/v1/apps/{slug}/settings          # Schema-driven settings (AppSettings)
PUT    /api/v1/apps/{slug}/settings          # Save settings overrides
POST   /api/v1/apps/{slug}/start             # Start app (process or route)
POST   /api/v1/apps/{slug}/stop              # Stop app (process or route)
POST   /api/v1/apps/{slug}/restart           # Restart (process-only)
POST   /api/v1/apps/{slug}/kill              # Kill (process-only)
POST   /api/v1/apps                          # Create app from registration
DELETE /api/v1/apps/{slug}                   # Stop-then-delete (10s timeout)
GET    /api/v1/apps/{slug}/logs              # Log snapshot (ring buffer, entries include id)
GET    /api/v1/apps/{slug}/logs/stream       # SSE log stream (?key= auth, ?lastEventId= resume)
GET    /api/v1/app-types                     # App type list (AppTypeListItem)
GET    /api/v1/app-types/{slug}/registration # Registration schema
GET    /api/v1/routes                        # Proxy route listing
POST   /api/v1/proxy/reload                  # Force proxy config regeneration
GET    /api/v1/dashboard/stats               # Dashboard statistics
GET    /api/v1/dashboard/events              # Dashboard activity-event feed
GET    /api/v1/events                        # Activity log query (filter + paginate)
GET    /api/v1/users                         # User list (admin-only)
POST   /api/v1/users                         # Create user (admin-only)
GET    /api/v1/users/{id}                    # Get user (admin-only)
PATCH  /api/v1/users/{id}/deactivate         # Deactivate user (admin-only)
GET    /api/v1/auth/me                       # Current authenticated user
GET    /api/v1/filesystem/browse             # Directory browse (registration path picker)
GET    /api/v1/filesystem/detect-strategy    # Recommended discovery strategy for a path
GET    /api/v1/status                        # System status (public, no auth)
GET    /api/v1/version                       # Build version (public, no auth)
ANY    /mcp                                  # MCP streamable HTTP endpoint (separate auth)
GET    /health                               # Health check
GET    /alive                                # Liveness check
```

**Conventions:** Lowercase status strings (`"running"` not `"Running"`). Slug-based routes. Responses include both `id` and `slug`.

## Frontend — War Machine Design System

- CSS custom properties for design tokens (colors, typography, spacing)
- IBM Plex Mono / Plex Sans fonts
- Component classes (`wm-*`) for visual identity, layout via standard CSS
- Biome for linting and formatting (not ESLint/Prettier)
- TanStack Query for data fetching (polling-based for most data); SSE is used for the per-app log stream via the `use-log-stream` hook (with `?lastEventId=` resume).
- React Router with slug-based routes

### Pages
- **Dashboard** — stats strip, app table, events feed
- **App List** — filter chips, search, sortable table, inline actions
- **App Detail** — identity header, action bar, stats strip, log viewer (SSE), route display, probe panels
- **App Settings** — schema-driven fields, FieldEditable variants, danger zone
- **App Create** — two-step flow at `/apps/new` (type picker → schema-driven form)
- **Routes** — route table, reload proxy button
- **System** — hostname, version, uptime
- **Users** — user list at `/users` (admin-only)
- **User Create** — new user at `/users/new` (admin-only)

## Collabhost Board

Work is tracked on the Collabhost Collaboard board. Bots and drones use the `collaboard` skill for board conventions, MCP usage, and drone-dispatch protocol.

| Field | Value |
|---|---|
| Slug | `collabhost` |
| Board UUID | `9868de95-1cb0-408f-9776-62eec8cfb9b8` |

Auth key: `.agents.env` → `COLLABOARD_AUTH_KEY` (gitignored, per-operator). Drones receive the key in the dispatch prompt.

### Project lane deviation (Collabhost-only)

This project deviates from the org-default lane set defined in the `collaboard` skill. Two changes:

- **Added: `On Deck`** — sits between `Triage` and `Ready`. Acts as the bench / depth chart for work that's been decided-on but isn't the immediate next pickup.
- **Removed: `Review`** — review happens on PRs, not on a board lane. The lane was unused.

**Lane order:** `Backlog` → `Triage` → `On Deck` → `Ready` → `In Progress` → `Done` → `Archived`.

**Lane semantics:**

| Lane | Meaning |
|---|---|
| Backlog | Someday/maybe. No commitment to ship. |
| Triage | New, awaiting disposition. Default exit is **On Deck** (or Backlog if long-tail, or Archived if rejected). |
| On Deck | Decided to do, queued for the cycle, not the immediate next. |
| Ready | Picked up next session (or right now). Curated by the operator; coordinator proposes promotions from On Deck. Target depth: 3-5 cards. |
| In Progress | Active work in flight. |
| Done | Shipped, awaiting archive. |
| Archived | Closed. |

**Coordinator implications:**

- Triage walks sub-batch dispositions as `On Deck / Backlog / Archive`, not `Ready / Backlog / Archive`.
- `HANDOFF.md` "what's next session" is a snapshot of the Ready lane — no mental subsetting required.
- When proposing card creation, default destination is On Deck unless the card is the imminent next pickup.
- At session-end, glance at On Deck depth — if it's growing past ~10 cards without churn, surface it (it shouldn't quietly become a second Backlog).

If On Deck proves out and other Collabot-org projects adopt it, promote the lane definition into the `collaboard` skill at that point.

## Named Agents

Currently-active named agents on Collabhost, with workspaces under `.agents/agents/{name}/`:

| Agent | Role | Specialty |
|-------|------|-----------|
| **Remy** | Backend lead | .NET, subsystem architecture, proxy, supervisor |
| **Dana** | Frontend lead | React, War Machine design system, UX |
| **Marcus** | Backend advisor | Architecture review, domain modeling |
| **Kai** | Code reviewer | Simplification, challenging assumptions |
| **Nolan** | Coordinator | Dispatch, triage, session handoffs, board curation |
| **Theo** | Tooling | Harness/tool experiments, skill authoring, evals |

Agents sign commits with `Co-Authored-By: {Name} <{name}@collabot.dev>`.

## Dispatch Tracking

All sub-agent dispatches are logged in `.agents/agents/nolan/DISPATCH_LOG.md` with pre-dispatch token estimates vs. actual consumption. This is mandatory — Nolan must update the log for every dispatch, every session. The log drives model selection calibration and task sizing over time.

## Deferred Features

See `v2-architecture.md` § Deferred Features for full list. Key items:
- Health check execution (capability schema exists, no runtime executor wired)
- App updates / deployment hooks (subsystem stubbed under `Updates/`, not yet wired)
- Real-time push for dashboard (log streaming ships via SSE; dashboard stats and routes still poll)
- Action error feedback for failed mutations (card #101)

## Relationship to Other Projects

| Project | Path | Relationship |
|---------|------|-------------|
| **Collaboard** | `../collaboard` | Kanban tracking via MCP. Work tracked on `collabhost` board. |
| **Collabot** | `../collabot` | Agent platform. Will consume Collabhost for service orchestration. |
| **Collabot TUI** | `../collabot-tui` | Terminal UI for Collabot. |
| **Ecosystem** | `../ecosystem` | Shared tooling. Collabhost may consume ecosystem scripts. |
| **Research Lab** | `../lab` | Research workspace. Architecture decisions researched here. |
| **Knowledge Base** | `../kb` | Conventions and patterns. |

## Persistence Rules

**IMPORTANT:** Auto-memory (`.claude/projects/.../memory/`) is ONLY for soft personal preferences (e.g., "operator likes terse responses", "operator prefers interrogation-style planning"). All project decisions, conventions, workflows, and hard rules MUST go into project infrastructure files: `CLAUDE.md`, roadmap, specs. If it's about how the project works, update the relevant infra doc — not memory.

## Agent Behavior Rules

1. **Safety over speed.** Never auto-fix lint errors — report to the operator.
2. **No destructive actions without explicit permission.**
3. **Ask, don't guess.** If uncertain about scope, intent, or approach, stop and ask.
4. **On any issues, errors, or unexpected behavior — stop and ask.**
5. **Max 3 follow-ups before escalation.**
6. **Never dismiss observed issues as "pre-existing."** Investigate, link to an existing card, or file a new card.
7. **UAT feedback accumulation.** During UAT, the operator gives feedback one item at a time, batched by recipient agent. Do NOT dispatch fixes until the operator explicitly says to dispatch — there may be more items to add.
8. **Build verification reads FULL output.** Always run `dotnet build Collabhost.slnx --no-incremental` and read the FULL output — not just the summary line. Surface ALL warnings from ANY source.

## Git, Paths, and Context Window

- Conventional commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`), squash merge to main.
- **Always merge PRs via `gh pr merge --squash`**, never local `git merge --squash` (local squash merges leave PRs dangling open on GitHub).
- Branch naming: `feature/`, `bugfix/`, `hotfix/`, `chore/`.
- **Commit everything** — `git status` must be clean when done.
- Relative paths in committed files; absolute paths only in gitignored config.
- Stay scoped to backend OR frontend — don't read both subsystems.
