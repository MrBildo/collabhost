# Collabhost — Self-Hosted Application Platform

## Identity

Collabhost is a **mini self-hosted application platform** — a control plane for managing local services, workers, MCP servers, and scheduled jobs from a single dashboard. It is not a full PaaS. It manages processes on the host machine, routes traffic through Caddy, and provides operational visibility.

**Mental model:**
- **Caddy** = front door (edge reverse proxy, TLS, routing)
- **ASP.NET Core** = control tower (app registry, process supervision, health, auth)
- **Hangfire** = automation engine (scheduled/recurring/delayed jobs)
- **React dashboard** = operator console (app cards, log tailing, route/job views)
- **SQLite** = persistence (zero-config)

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10 / C# (Minimal API, EF Core, SQLite) |
| Frontend | React 18 + TypeScript, Vite, Tailwind, shadcn/ui |
| Testing | xUnit + Shouldly, Arrange-Act-Assert |
| Orchestration | Aspire 13.1, OpenTelemetry |
| Jobs | Hangfire (persistent, recurring, dashboard built-in) |
| Reverse Proxy | Caddy (edge), managed externally |
| Agent protocol | MCP (HTTP transport) |

## Structure

```
collabhost/
├── CLAUDE.md                  # This file
├── COLLABOARD.md              # Kanban board conventions
├── README.md                  # Quick start
├── LICENSE
├── .editorconfig              # Code style (tool-enforced)
├── nuget.config               # NuGet feed config (Aspire preview)
├── .gitignore
├── .mcp.json                  # MCP server config (gitignored)
├── .agents.env                # Auth keys (gitignored)
├── .claude/
│   └── settings.local.json    # Tool permissions (gitignored)
├── .agents/                   # Instance-local workspace (gitignored)
│   ├── roadmap/INDEX.md
│   ├── specs/TEMPLATE.md
│   ├── archive/
│   ├── research/
│   ├── kb/
│   └── temp/
├── backend/
│   ├── Collabhost.slnx
│   ├── Collabhost.AppHost/    # Aspire orchestrator
│   ├── Collabhost.ServiceDefaults/  # Shared telemetry/health
│   ├── Collabhost.Api/        # Main API project
│   │   ├── Endpoints/         # Endpoint group classes
│   │   ├── Models/            # Entity/response models
│   │   ├── Migrations/        # EF Core migrations
│   │   ├── data/              # SQLite database (gitignored)
│   │   └── appsettings.json
│   └── Collabhost.Api.Tests/  # Integration tests
├── frontend/
│   ├── package.json
│   ├── vite.config.ts
│   ├── tailwind.config.ts
│   ├── src/
│   │   ├── components/
│   │   ├── hooks/
│   │   ├── routes/
│   │   ├── types/
│   │   └── lib/
│   └── public/
└── docs/                      # Decision artifacts
```

## Build & Run

### Recommended: Aspire

```powershell
dotnet run --project backend/Collabhost.AppHost
```

This starts the API, frontend dev server, and Aspire dashboard with OpenTelemetry.

### Standalone

```powershell
# Backend only
dotnet run --project backend/Collabhost.Api

# Frontend only
cd frontend && npm run dev
```

### Tests

```powershell
cd backend && dotnet test
cd frontend && npm run test
```

## Core Modules

### App Registry
- App definitions: name, type (web, worker, mcp, static, proxy-only), executable, args, working directory, environment variables, local bind port, route metadata, health endpoint, auto-start, restart policy

### Process Supervisor
- Start/stop/restart managed processes
- Capture stdout/stderr
- Crash detection + restart with backoff
- PID tracking

### Scheduler (Hangfire)
- Cron/interval recurring jobs
- One-off delayed jobs
- Job history + result logging
- Built-in Hangfire dashboard

### Dashboard API
- Status overview
- Log tailing
- App config CRUD
- Route listing
- Job management

### Proxy Config (Caddy)
- Generate/update Caddy config for managed apps
- Route: `app.lan → localhost:<port>`
- Collabhost manages the config, Caddy handles the traffic

## Auth Model

Header-based: `X-User-Key` (ULID). Same pattern as Collaboard.

- Roles: Administrator, HumanUser, AgentUser
- No ASP.NET auth middleware — custom logic
- Admin seed from config or generated key (logged on first run)
- Use `Results.StatusCode(403)` not `Results.Forbid()`

## API Surface

REST API under `/api/v1/`:

```
/api/v1/apps          # App registry CRUD
/api/v1/processes     # Process lifecycle (start/stop/restart/status)
/api/v1/jobs          # Job scheduling and history
/api/v1/routes        # Proxy route configuration
/api/v1/logs          # Log retrieval and tailing
/api/v1/health        # System health overview
```

MCP endpoint: `/mcp`

## Endpoint Structure

Static classes under `backend/Collabhost.Api/Endpoints/`. One file per resource.

```csharp
public static class AppEndpoints
{
    public static RouteGroupBuilder MapAppEndpoints(this RouteGroupBuilder group) { ... }
}
```

`Program.cs` is a thin composition root — it wires services and maps endpoint groups.

## C# Conventions

- File-scoped namespaces
- Primary constructors
- Pattern matching (`is null`, `is not null`)
- No XML doc comments
- `var` everywhere
- Private fields: `_camelCase`
- Other members: PascalCase
- Interfaces: `IPascalCase`
- Guard clauses: `ArgumentNullException.ThrowIfNull()`
- Collection expressions: `[]`
- Expression-bodied members for one-liners
- `.editorconfig` enforced

## Frontend Style

- Functional components with hooks
- shadcn/ui from `@/components/ui/`
- `cn()` for Tailwind class merging
- 2-space indentation
- TanStack Query for API calls

## Definition of Done

- `dotnet build` (backend) — no errors, no warnings
- `npm run build` (frontend — typecheck + Vite)
- `dotnet test` (backend)
- `npm run test` (frontend)
- `npm run lint && npm run format:check` (frontend)
- Format: `dotnet format` + `npm run format`

## Plugin Model (v1)

Simple DI-based registration. No hot-loading.

```csharp
public interface IAppProvider { ... }
public interface IJobProvider { ... }
public interface IRouteContributor { ... }
public interface IHealthContributor { ... }
```

Modules register routes, processes, scheduled jobs, dashboard widgets, and health checks through standard .NET DI.

## What Collabhost Owns vs. Delegates

**Owns:** App modeling, process supervision, dashboard UX, scheduling integration, proxy config generation, auth, plugin model.

**Delegates:** Raw HTTP serving (Kestrel), TLS termination (Caddy), reverse proxy transport (Caddy), cron parsing (Hangfire), log storage engine (future — Loki or similar).

## Agent Behavior Rules

1. **Safety over speed.** Never auto-fix lint errors — report to user.
2. **No destructive actions without explicit permission.**
3. **Ask, don't guess.** If uncertain about scope, intent, or approach, stop and ask.
4. **On any issues, errors, or unexpected behavior — stop and ask.**
5. **Max 3 follow-ups before escalation.**

## Relationships

| Project | Path | Relationship |
|---------|------|-------------|
| **Collaboard** | `../collaboard` | Kanban tracking via MCP SSE. Work tracked on `collabhost` board. |
| **Collabot** | `../collabot` | Agent platform. Will consume Collabhost for service orchestration. |
| **Ecosystem** | `../ecosystem` | Shared tooling. Collabhost may consume ecosystem scripts. |
| **Lab** | `../lab` | Research workspace. Architecture decisions researched here. |
| **KB** | `../kb` | Conventions and patterns. |

## Git Rules

- Branch naming: `feature/`, `bugfix/`, `hotfix/`
- Conventional commits: `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`
- Squash merge to main
- All changes via feature branch + PR

## Path Conventions

- **Relative paths in docs.** Never hardcode absolute paths in committed files.
- **Cross-project references:** `../` relative paths (e.g., `../collaboard`, `../ecosystem`).
- **Absolute paths only** in gitignored local configuration (`.agents.env`, local settings).
