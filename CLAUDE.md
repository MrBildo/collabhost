# Collabhost — Self-Hosted Application Platform

## Identity

Collabhost is a **mini self-hosted application platform** — a control plane for managing local services, workers, MCP servers, and scheduled jobs from a single dashboard. It is not a full PaaS. It manages processes on the host machine, routes traffic through Caddy, and provides operational visibility.

See [[.agents/WORKFLOW]] for planning process. See [[COLLABOARD]] for board conventions. See [[COLLABHOST_KB]] for all coding conventions.

## Coding Conventions & Agent Compliance

**ALL coding agents and sub-agents MUST follow [[COLLABHOST_KB]] when writing code.** This is non-negotiable. The KB covers .NET/C#, TypeScript/React, and general conventions in full detail.

In addition to the project KB, coding agents MUST use the appropriate skill for the task:
- **C#/.NET tasks:** Invoke the `dotnet-dev` skill AND follow `COLLABHOST_KB.md` §1
- **TypeScript/React tasks:** Invoke the `typescript-dev` skill AND follow `COLLABHOST_KB.md` §2
- **All tasks:** Follow `COLLABHOST_KB.md` §3 (general conventions, verification, safety)

When the KB and a skill conflict, the KB wins — it contains project-specific overrides. When dispatching sub-agents, ALWAYS include this instruction in the prompt:

> You MUST read and follow `COLLABHOST_KB.md` before writing any code. Use the `dotnet-dev` skill for C# or `typescript-dev` skill for TypeScript. Run ALL verification steps from the KB §3 Definition of Done before reporting done.

**Mental model:**
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

Development is on `release/v2`. The v1 codebase is preserved on `v0-reference` for reference only.

## Structure

```
collabhost/
├── CLAUDE.md                  # This file
├── COLLABHOST_KB.md           # Coding conventions (source of truth for agents)
├── COLLABOARD.md              # Kanban board conventions
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

### Tests & Verification

```powershell
# Backend (MUST use solution build with --no-incremental)
cd backend && dotnet build Collabhost.slnx --no-incremental
cd backend && dotnet format --verify-no-changes
cd backend && dotnet test   # Runs both Api.Tests (320) and AppHost.Tests (12)

# Frontend
cd frontend && npm run build
cd frontend && npm run lint
cd frontend && npm run format:check
cd frontend && npm run test   # 129 tests
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

## Named Agents

Four named agents with dedicated workspaces in `.agents/agents/{name}/`:

| Agent | Role | Specialty |
|-------|------|-----------|
| **Remy** | Backend lead | .NET, subsystem architecture, proxy, supervisor |
| **Dana** | Frontend lead | React, War Machine design system, UX |
| **Marcus** | Backend advisor | Architecture review, domain modeling |
| **Kai** | Code reviewer | Simplification, challenging assumptions |

Agents sign commits with `Co-Authored-By: {Name} <{name}@collabot.dev>`.

## Deferred Features

See `v2-architecture.md` § Deferred Features for full list. Key items:
- Health check execution (capability schema exists, no runtime logic)
- App metadata probing (spec at `app-metadata-probing.md`, in-memory cache design decided)
- App updates / SSE deployment (deferred from v2)
- Real-time push (SSE/WebSocket for dashboard)
- Action error feedback for failed mutations (card #101)

## Known Issues

- **Card #82:** WindowsProcessRunner doesn't use Job Objects — child processes orphaned on API restart
- **Card #83:** Caddy admin port hardcoded to 2019 — should be dynamically allocated

## Relationship to Other Projects

| Project | Path | Relationship |
|---------|------|-------------|
| **Collaboard** | `../collaboard` | Kanban tracking via MCP SSE. Work tracked on `collabhost` board. |
| **Collabot** | `../collabot` | Agent platform. Will consume Collabhost for service orchestration. |
| **Collabot TUI** | `../collabot-tui` | Terminal UI for Collabot. |
| **Ecosystem** | `../ecosystem` | Shared tooling. Collabhost may consume ecosystem scripts. |
| **Research Lab** | `../lab` | Research workspace. Architecture decisions researched here. |
| **Knowledge Base** | `../kb` | Conventions and patterns. |

## Persistence Rules

**IMPORTANT:** Auto-memory (`.claude/projects/.../memory/`) is ONLY for soft personal preferences (e.g., "user likes terse responses", "user prefers interrogation-style planning"). All project decisions, conventions, workflows, and hard rules MUST go into project infrastructure files: `CLAUDE.md`, `.agents/WORKFLOW.md`, `COLLABOARD.md`, roadmap, specs, etc. If it's about how the project works, update the relevant infra doc — not memory.

## Agent Behavior Rules

See **[[COLLABHOST_KB]]** §3 Safety for the full list. Summary:

1. **Safety over speed.** Never auto-fix lint errors — report to user.
2. **No destructive actions without explicit permission.**
3. **Ask, don't guess.** If uncertain about scope, intent, or approach, stop and ask.
4. **On any issues, errors, or unexpected behavior — stop and ask.**
5. **Max 3 follow-ups before escalation.**

## Git, Paths, and Context Window

See **[[COLLABHOST_KB]]** §3 for full rules. Key points:

- Conventional commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`), squash merge to main
- Branch naming: `feature/`, `bugfix/`, `hotfix/`
- **Commit everything** — `git status` must be clean when done
- Relative paths in committed files; absolute paths only in gitignored config
- Stay scoped to backend OR frontend — don't read both subsystems
