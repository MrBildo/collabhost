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
| Orchestration | Aspire 13.3, OpenTelemetry |
| Jobs | Hangfire (persistent, recurring, dashboard built-in) |
| Reverse Proxy | Caddy (edge), managed by Collabhost via JSON API |
| Agent protocol | MCP (HTTP transport) |

## Structure

```
collabhost/
├── CLAUDE.md                  # This file
├── COLLABHOST_KB.md           # Coding conventions (source of truth for agents)
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
│   │   ├── Common/            # Command dispatcher (_Commands.cs), auth (_Authorization.cs)
│   │   ├── Features/          # Vertical slices grouped by domain (Apps/, Proxy/, System/)
│   │   ├── Domain/            # Entities, lookups, value objects (consolidated _-prefixed files)
│   │   ├── Data/              # EF Core DbContext (includes extensions)
│   │   ├── Migrations/        # EF Core migrations
│   │   ├── Services/          # Cross-cutting services + DI registration (_ServiceRegistration.cs)
│   │   ├── db/                # SQLite database (gitignored)
│   │   └── appsettings.json
│   ├── Collabhost.Api.Tests/  # Integration tests (WebApplicationFactory + fakes)
│   └── Collabhost.AppHost.Tests/  # Aspire smoke tests (real Kestrel)
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

`dotnet test` runs both test projects:
- `Collabhost.Api.Tests` — in-memory integration tests with fakes (fast, no external deps)
- `Collabhost.AppHost.Tests` — Aspire smoke tests against real Kestrel (boots the AppHost, real SQLite + process runner)

## Core Modules

### Command Dispatcher
- `ICommand<TResult>` / `ICommandHandler<TCommand, TResult>` — unified command pattern for ALL operations (reads and writes)
- `CommandDispatcher` with type-inferring `DispatchAsync` (resolves handlers via DI)
- `AddCommandDispatcher()` extension — auto-scans assembly for handler registrations
- `Empty` struct for void-result commands (`ICommand<Empty>`)
- `CommandResult<T>` — result type with success/fail factory methods
- All types in `Common/_Commands.cs` (dispatcher + result types + registration extension)

### App Registry
- App definitions: name (slug, used in domain), display name, type (Executable, NpmPackage, StaticSite), install directory, command, args, working directory, environment variables (separate table rows), port (auto-assigned via bind-to-zero), health endpoint, update command, update timeout (per-app, nullable, defaults 300s), auto-start, restart policy (Never/OnCrash/Always)
- ULID primary keys

### Process Supervisor
- Start/stop/restart managed processes
- Capture stdout/stderr into in-memory ring buffer per process
- Crash detection + restart with exponential backoff
- PID + uptime tracking, restart count with grace period
- Env var injection at process launch
- Auto-start on Collabhost startup
- StaticSite type: no process (Caddy serves directly)

### Scheduler (Hangfire) — post-MVP
- Cron/interval recurring jobs
- One-off delayed jobs
- Job history + result logging
- Built-in Hangfire dashboard

### App Updates
- `POST /api/v1/apps/{id}/update` — SSE-streamed orchestration
- Flow: stop (if running) → run UpdateCommand via shell → start (if was running)
- Real-time SSE events: `status` (phase changes), `log` (stdout/stderr lines), `result` (final outcome)
- Per-app concurrency guard (`UpdateCoordinator` singleton, rejects 409)
- Per-app `UpdateTimeoutSeconds` (nullable, defaults to 300s)
- Shell wrapping: `cmd.exe /c` on Windows, `/bin/sh -c` on Linux
- Channel-based ordered SSE delivery (not fire-and-forget)
- `RunToCompletionAsync` on `IManagedProcessRunner` for one-shot commands

### Dashboard API
- Status overview
- Log retrieval (ring buffer)
- App config CRUD
- Route listing
- App update orchestration

### Proxy Config (Caddy)
- Collabhost manages Caddy as a supervised process
- Config via Caddy JSON API at `localhost:2019` (not Caddyfile)
- `@id` tags on every route for direct CRUD
- Route: `<app>.collab.internal` → `reverse_proxy localhost:<port>` or `file_server`
- HTTPS via Caddy internal CA + `tls internal`
- Base domain configurable (default `collab.internal`)

## Auth Model

Header-based: `X-User-Key` (ULID). Same pattern as Collaboard.

- Roles: Administrator, HumanUser, AgentUser
- No ASP.NET auth middleware — custom logic
- Admin seed from config or generated key (logged on first run)
- Use `Results.StatusCode(403)` not `Results.Forbid()`

## API Surface

REST API under `/api/v1/`:

```
/api/v1/apps                # App registry CRUD
/api/v1/apps/{id}/start     # Start app process
/api/v1/apps/{id}/stop      # Stop app process
/api/v1/apps/{id}/restart   # Restart app process
/api/v1/apps/{id}/status    # Process state, PID, uptime
/api/v1/apps/{id}/logs      # Log retrieval (ring buffer)
/api/v1/apps/{id}/update    # Run update script (SSE-streamed)
/api/v1/routes              # Proxy route listing
/api/v1/proxy/reload        # Force proxy config regeneration
/api/v1/status              # System status
/health                     # Health check
```

## Endpoint Structure (Vertical Slices)

Organized under `backend/Collabhost.Api/Features/`, grouped by domain (`Apps/`, `Proxy/`, `System/`). One file per operation (e.g., `Create.cs`, `GetAll.cs`, `Delete.cs`).

Each file (command or query) contains three top-level types:
1. **Static endpoint class** — `Request`/`Response` records + `HandleAsync` (HTTP layer only, dispatches via `CommandDispatcher`)
2. **Command record** — implements `ICommand<TResult>` (use `ICommand<Empty>` for void-result commands)
3. **Handler class** — implements `ICommandHandler<TCommand, TResult>`, contains all business logic

Each domain folder has a `_Module.cs` implementing `IFeatureModule` to map routes. `Program.cs` is a thin composition root — it wires services and auto-discovers feature modules.

## Coding Conventions Reference

All coding conventions (C#, TypeScript, general) are in **[[COLLABHOST_KB]]**. Do not duplicate them here.

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

## Dispatching Work

Dispatch rules, sub-agent conventions, report format, and parallel dispatch rules are in **[[COLLABHOST_KB]]** §3. Key points:

- **Spec first** — no dispatch without a spec in `.agents/specs/`
- **Model:** Always `model: "opus"` for coding sub-agents
- **Skills:** Sub-agents MUST use `dotnet-dev` for C# and `typescript-dev` for TypeScript
- **KB mandate:** Every dispatch prompt MUST instruct the agent to read and follow `COLLABHOST_KB.md`
- **Partition by resource** — if two agents could write to the same file, they must be the same agent
- **Standardized report format** — see KB §3 for the template

## Persistence Rules

**IMPORTANT:** Auto-memory (`.claude/projects/.../memory/`) is ONLY for soft personal preferences (e.g., "user likes terse responses", "user prefers interrogation-style planning"). All project decisions, conventions, workflows, and hard rules MUST go into project infrastructure files: `CLAUDE.md`, `.agents/WORKFLOW.md`, `COLLABOARD.md`, roadmap, specs, etc. If it's about how the project works, update the relevant infra doc — not memory.

## Agent Behavior Rules

See **[[COLLABHOST_KB]]** §3 Safety for the full list. Summary:

1. **Safety over speed.** Never auto-fix lint errors — report to user.
2. **No destructive actions without explicit permission.**
3. **Ask, don't guess.** If uncertain about scope, intent, or approach, stop and ask.
4. **On any issues, errors, or unexpected behavior — stop and ask.**
5. **Max 3 follow-ups before escalation.**

## Relationship to Other Projects

| Project | Path | Relationship |
|---------|------|-------------|
| **Collaboard** | `../collaboard` | Kanban tracking via MCP SSE. Work tracked on `collabhost` board. |
| **Collabot** | `../collabot` | Agent platform. Will consume Collabhost for service orchestration. |
| **Collabot TUI** | `../collabot-tui` | Terminal UI for Collabot. |
| **Ecosystem** | `../ecosystem` | Shared tooling. Collabhost may consume ecosystem scripts. |
| **Research Lab** | `../lab` | Research workspace. Architecture decisions researched here. |
| **Knowledge Base** | `../kb` | Conventions and patterns. |

## Git, Paths, and Context Window

See **[[COLLABHOST_KB]]** §3 for full rules. Key points:

- Conventional commits (`feat:`, `fix:`, `chore:`, `docs:`, `refactor:`), squash merge to main
- Branch naming: `feature/`, `bugfix/`, `hotfix/`
- **Commit everything** — `git status` must be clean when done
- Relative paths in committed files; absolute paths only in gitignored config
- Stay scoped to backend OR frontend — don't read both subsystems
