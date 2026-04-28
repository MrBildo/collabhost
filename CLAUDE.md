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

## Coding Conventions

### Skills agents must use

- **C# / .NET work:** invoke the `dotnet-dev` skill (code style + tooling) AND `do-dotnet-backend-architecture` (subsystem shape, domain modeling, EF Core, API contracts).
- **TypeScript / React work:** invoke the `typescript-dev` skill.

The skills carry the universal patterns. The sections below name only Collabhost-specific overrides.

### .NET overrides

#### `App` is the product entity name (naming exception)

The domain entity `App` keeps its short form despite the `dotnet-dev` no-abbreviations rule. `App` is the product's named concept (`AppType`, `AppStore`, `Apps/` feature folder). `Application` is reserved for ASP.NET / hosting concepts.

#### Lookup tables over enums (project-wide deviation)

Collabhost uses lookup tables for fixed catalogs (app types, role types, restart policies) instead of the architecture skill's default of enums. We deviate because every catalog value carries display labels, ordering, and activation flags consumed by the schema-driven frontend, and we want one source-of-truth shape from DB to UI.

#### Authorization

- Header-based: `X-User-Key` (ULID).
- Three role types: `Administrator`, `HumanUser`, `AgentUser`.
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
| Frontend | React 18 + TypeScript, Vite, Biome, War Machine design system |
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
│   │   ├── Authorization/     # Auth middleware (X-User-Key header, ?key= for SSE only)
│   │   ├── Capabilities/      # Catalog, config types, schemas, resolver
│   │   ├── Dashboard/         # Dashboard stats endpoint
│   │   ├── Data/              # EF Core DbContext, seed data, migrations
│   │   ├── Events/            # Generic typed event bus
│   │   ├── Proxy/             # ProxyManager, CaddyClient, config builder, seeder
│   │   ├── Registry/          # App/AppType entities, AppStore, CRUD endpoints
│   │   ├── Shared/            # RingBuffer, LogEntry
│   │   ├── Supervisor/        # ProcessSupervisor, ManagedProcess, PortAllocator
│   │   ├── System/            # Status endpoint
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
│       ├── hooks/             # TanStack Query hooks per domain
│       ├── log/               # LogViewer, LogLine
│       ├── pages/             # All page components
│       ├── shared/            # EmptyState, ErrorBanner, ConfirmDialog, etc.
│       ├── status/            # StatusDot, StatusText, StatusStrip, StatsStrip
│       ├── styles/            # War Machine tokens, components, reset
│       └── tables/            # DataTable, FilterBar, app-columns
└── tools/
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
- `App`, `AppType` entities with ULID primary keys, slug-based identity
- `AppStore` singleton with `IDbContextFactory<AppDbContext>` + `IMemoryCache`
- 5 built-in app types: `dotnet-app`, `nodejs-app`, `static-site`, `executable`, `system-service`
- Slug: unique, immutable, `[a-z0-9-]`, used in API routes and domain names

### Capability System (`Capabilities/`)
- 8 behavioral capabilities: process, port-injection, routing, health-check, environment-defaults, restart, auto-start, artifact
- Static `CapabilityCatalog` (code-only, no DB table)
- `CapabilityBinding` (type-level defaults) + `CapabilityOverride` (per-app overrides)
- `CapabilityResolver.Resolve<T>()` — pure static function, JSON merge, no I/O
- Schema-driven: co-located `Schema` static property on each config type drives form generation and validation

### Process Supervisor (`Supervisor/`)
- Singleton `IHostedService`, `ConcurrentDictionary<Ulid, ManagedProcess>`
- Start/stop/restart/kill managed processes
- `IManagedProcessRunner` interface — platform-specific implementations (`WindowsProcessRunner`, `LinuxProcessRunner` planned)
- `IProcessContainment` / `IContainmentHandle` — process containment abstraction (Windows: Job Objects, Linux: planned)
- Windows: CreateProcess P/Invoke, process groups, `GenerateConsoleCtrlEvent` for graceful shutdown, Job Objects for orphan protection
- Stdout/stderr capture into in-memory ring buffer
- Crash detection + restart with exponential backoff
- Discovery strategy via switch expression (DotNetRuntimeConfiguration, PackageJson, Manual)
- Port allocation via bind-to-zero (`PortAllocator`)
- Auto-start on Collabhost startup
- Static sites: start/stop toggles Caddy routing (no process)

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
- `GET /dashboard/stats` — aggregated counts (total, running, stopped, crashed)

### Auth (`Authorization/`)
- Header-based: `X-User-Key` (ULID)
- Query param `?key=` fallback scoped to SSE endpoint (`/logs/stream`) only
- Middleware skips `/health`, `/alive`, `/openapi`, `GET /status`
- Admin key from config (`Auth:AdminKey`)

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
GET    /api/v1/status                        # System status (public, no auth)
GET    /health                               # Health check
GET    /alive                                # Liveness check
```

**Conventions:** Lowercase status strings (`"running"` not `"Running"`). Slug-based routes. Responses include both `id` and `slug`.

## Frontend — War Machine Design System

- CSS custom properties for design tokens (colors, typography, spacing)
- IBM Plex Mono / Plex Sans fonts
- Component classes (`wm-*`) for visual identity, layout via standard CSS
- Biome for linting and formatting (not ESLint/Prettier)
- TanStack Query for data fetching (polling-based, no SSE yet)
- React Router with slug-based routes

### Pages
- **Dashboard** — stats strip, app table, events feed
- **App List** — filter chips, search, sortable table, inline actions
- **App Detail** — identity header, action bar, stats strip, log viewer, route display
- **App Settings** — schema-driven fields, FieldEditable variants, danger zone
- **App Registration** — two-step flow (type picker → schema-driven form)
- **Routes** — route table, reload proxy button
- **System** — hostname, version, uptime

## Collabhost Board

Work is tracked on the Collabhost Collaboard board. Bots and drones use the `collaboard` skill for board conventions, MCP usage, and drone-dispatch protocol.

| Field | Value |
|---|---|
| Slug | `collabhost` |
| Board UUID | `9868de95-1cb0-408f-9776-62eec8cfb9b8` |

Auth key: `.agents.env` → `COLLABOARD_AUTH_KEY` (gitignored, per-operator). Drones receive the key in the dispatch prompt.

Project deviations from Collabot-org board defaults: none currently.

## Named Agents

Currently-active named agents on Collabhost, with workspaces under `.agents/agents/{name}/`:

| Agent | Role | Specialty |
|-------|------|-----------|
| **Remy** | Backend lead | .NET, subsystem architecture, proxy, supervisor |
| **Dana** | Frontend lead | React, War Machine design system, UX |
| **Marcus** | Backend advisor | Architecture review, domain modeling |
| **Kai** | Code reviewer | Simplification, challenging assumptions |

Agents sign commits with `Co-Authored-By: {Name} <{name}@collabot.dev>`.

## Dispatch Tracking

All sub-agent dispatches are logged in `.agents/agents/nolan/DISPATCH_LOG.md` with pre-dispatch token estimates vs. actual consumption. This is mandatory — Nolan must update the log for every dispatch, every session. The log drives model selection calibration and task sizing over time.

## Deferred Features

See `v2-architecture.md` § Deferred Features for full list. Key items:
- Health check execution (capability schema exists, no runtime logic)
- App metadata probing (spec at `app-metadata-probing.md`, in-memory cache design decided)
- App updates / SSE deployment (deferred from v2)
- Real-time push (SSE/WebSocket for dashboard)
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
