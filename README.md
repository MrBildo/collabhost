<p align="center">
  <img src="frontend/public/favicon.svg" alt="Collabhost" width="80" />
</p>

<h1 align="center">Collabhost</h1>

<p align="center">
  A self-hosted control plane for your local services, MCP servers, and AI agent workflows.<br/>
  Cross-platform. Windows and Linux.
</p>

<p align="center">
  <!-- TODO: Replace with real badge URLs once CI and package registry are set up -->
  <a href="#"><img alt="Build" src="https://img.shields.io/badge/build-passing-brightgreen?style=flat-square" /></a>
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" /></a>
  <a href="#"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square" /></a>
  <a href="#"><img alt="React 18" src="https://img.shields.io/badge/React-18-61DAFB?style=flat-square" /></a>
</p>

---

<!-- TODO [Bill]: Write the real overview copy. The structure below is the target
     tone — sell first (what problem does this solve?), then explain (how?).
     Two paragraphs max before the screenshot. -->

## What is Collabhost?

Collabhost gives you a single dashboard to manage everything running on your machine — .NET services, Node.js apps, MCP servers, static sites, and arbitrary executables. Register an app, point it at a directory, and Collabhost handles process supervision, reverse proxy routing, log aggregation, and crash recovery. No containers. No YAML. No cloud account.

**Built for AI agent workflows.** Collabhost is designed with AI agents as first-class operators. Manage MCP servers alongside your application stack, provide agents with API access through scoped user keys, and give both humans and agents a unified control plane for the services they depend on. If you're building an AI harness, agent framework, or multi-agent system that needs to manage local infrastructure, Collabhost is the platform layer.

It runs natively on **Windows** and **Linux** with platform-specific process management — no WSL required on Windows, no emulation layer on Linux. Think of it as a lightweight, self-hosted Heroku for your workstation — a control plane that stays out of your way until something goes wrong.

<!-- SCREENSHOT SECTION
     ==================
     Bill — here's what I recommend capturing:

     1. **Hero screenshot** (required)
        The Dashboard page with 4-6 apps in mixed states (running, stopped, one crashed).
        This shows the stats strip, app table, and activity feed all at once.
        Ideal viewport: 1280x800 or 1440x900. Dark theme (it's the only theme).
        Save as: docs/images/dashboard.png

     2. **App detail screenshot** (optional but high impact)
        An app detail page for a running Node.js or .NET app showing:
        - The identity header with status dot and action bar
        - A few lines of log output in the log viewer
        - The route indicator showing the proxy domain
        Save as: docs/images/app-detail.png

     3. **Registration flow** (optional)
        The two-step registration: type picker grid, then the schema-driven form.
        Shows that adding a new app is a 30-second operation.
        Save as: docs/images/register-app.png

     Place screenshots in a docs/images/ directory.
     Delete this comment block after adding them.
-->

<p align="center">
  <!-- TODO: Replace with actual screenshot once captured -->
  <img src="docs/images/dashboard.png" alt="Collabhost Dashboard" width="900" />
</p>

## Features

**Process supervision** — Start, stop, restart, and kill managed processes with platform-native implementations for both Windows and Linux. On Windows: processes launch via `CreateProcess` with dedicated process groups, graceful shutdown via `GenerateConsoleCtrlEvent`, and orphan protection through Win32 Job Objects that guarantee child process cleanup even if Collabhost crashes. On Linux: process groups with `SIGTERM`/`SIGKILL` lifecycle and cgroup-based containment. Crash detection with automatic restart and configurable exponential backoff. Stdout/stderr captured into in-memory ring buffers with streaming log viewers.

**Reverse proxy** — Every app gets a subdomain route automatically configured through [Caddy](https://caddyserver.com/). HTTPS via Caddy's internal CA. Routes sync on process state changes — no manual proxy config. The base domain is configurable in `appsettings.json`.

**Schema-driven configuration** — App settings are defined by capability schemas. New capabilities surface in the UI without frontend changes. Override defaults per-app, see what's customized vs. inherited.

**Multi-runtime support** — Five built-in app types out of the box:

| Type | What it runs |
|------|-------------|
| `dotnet-app` | .NET applications (auto-discovers project files) |
| `nodejs-app` | Node.js applications (reads package.json scripts) |
| `static-site` | Static file directories (served via Caddy file_server) |
| `executable` | Arbitrary binaries and scripts |
| `system-service` | Platform services managed by Collabhost itself |

**Operational dashboard** — Real-time stats, app table with inline actions, and a live activity feed. Everything an operator needs on one screen.

**User management** — Header-based auth with admin and agent roles. One-time API key reveal on creation. Role-based access control for the dashboard.

**Technology probing** — Automatic detection of runtimes, frameworks, and dependencies for registered apps. No manual tagging required.

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/) (for the dashboard)
- [Caddy](https://caddyserver.com/) (recommended -- reverse proxy routing requires it, but Collabhost runs without it)

### Install Caddy

Collabhost manages Caddy as a supervised process -- you install the binary, Collabhost handles the rest. Without Caddy, everything else works (app management, process supervision, logs, dashboard), but apps won't get automatic `{slug}.collab.internal` subdomain routes.

**Windows:**

```powershell
winget install CaddyServer.Caddy
```

**Linux (Debian/Ubuntu):**

```bash
sudo apt install -y debian-keyring debian-archive-keyring apt-transport-https
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' | sudo gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg
curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' | sudo tee /etc/apt/sources.list.d/caddy-stable.list
sudo apt update && sudo apt install caddy
```

See [caddyserver.com/docs/install](https://caddyserver.com/docs/install) for other platforms.

If `caddy` is on your PATH, Collabhost finds it automatically. Otherwise, set the path in `appsettings.json`:

```json
{
  "Proxy": {
    "BinaryPath": "/path/to/caddy"
  }
}
```

Collabhost starts Caddy as a managed system-service, allocates its admin port dynamically, and handles all proxy configuration through Caddy's JSON API. No Caddyfile editing required.

### Run with Aspire

The recommended development workflow uses .NET Aspire to orchestrate the API, the Vite dev server, and an OpenTelemetry dashboard together.

```bash
# Clone the repo
git clone https://github.com/MrBildo/collabhost.git
cd collabhost

# Install frontend dependencies
cd frontend && npm install && cd ..

# Start everything (API + dashboard + telemetry)
dotnet run --project backend/Collabhost.AppHost
```

The Aspire dashboard URL is printed to the console at startup -- open it to see resource health, logs, and traces. The Collabhost dashboard is served by Vite on `http://localhost:5173`. The API runs on `http://localhost:58400`.

### Run standalone

If you don't need the Aspire orchestrator, you can run the API and frontend independently.

```bash
# Backend (in one terminal)
dotnet run --project backend/Collabhost.Api

# Frontend (in a second terminal)
cd frontend && npm install && npm run dev
```

The frontend dev server proxies API requests to `http://localhost:58400` automatically. Open `http://localhost:5173` to use the dashboard.

### Register your first app

Open the dashboard and click **Register App**. Pick an app type, point it at a directory, and hit create. Collabhost auto-discovers the start command and allocates a port. Click **Start** and watch the logs stream in.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10, C# Minimal API, EF Core, SQLite |
| Frontend | React 18, TypeScript, Vite |
| Design System | *War Machine* (custom — dark, monospace, industrial) |
| Reverse Proxy | Caddy (managed via JSON admin API) |
| Orchestration | .NET Aspire, OpenTelemetry |
| Testing | xUnit + Shouldly (backend), Vitest (frontend) |
| Linting | Roslyn analyzers (backend), Biome (frontend) |

## Project Structure

```
collabhost/
├── backend/
│   ├── Collabhost.AppHost/        # Aspire orchestrator
│   ├── Collabhost.Api/            # Main API (registry, supervisor, proxy)
│   ├── Collabhost.Api.Tests/      # Integration tests
│   └── Collabhost.ServiceDefaults/
├── frontend/
│   └── src/
│       ├── actions/               # Action buttons and bars
│       ├── api/                   # Typed fetch client and endpoints
│       ├── chrome/                # Layout, topbar, auth gate
│       ├── forms/                 # Schema-driven form fields
│       ├── hooks/                 # TanStack Query hooks
│       ├── log/                   # Log viewer with ANSI rendering
│       ├── pages/                 # Route pages
│       ├── shared/                # Shared UI components
│       ├── status/                # Status dots, strips, stats
│       ├── styles/                # *War Machine* design tokens
│       └── tables/                # Data tables and filters
```

## Development

### Build and test

```bash
# Backend
cd backend
dotnet build Collabhost.slnx --no-incremental
dotnet test

# Frontend
cd frontend
npm run build
npm run test
npm run lint
```

### Architecture

<!-- TODO [Bill]: Decide how deep to go here. Options:
     1. Brief paragraph pointing to a CONTRIBUTING.md or docs/ folder
     2. The mental model summary below (my recommendation for the README)
     3. A full architecture doc linked from here -->

Collabhost has three layers:

- **Caddy** is the front door — edge reverse proxy, TLS termination, routing
- **ASP.NET Core** is the control tower — app registry, process supervision, auth
- **React dashboard** is the operator console — the *War Machine* design system

Apps are registered with a slug, discovered from the filesystem, and supervised as managed processes. Caddy routes are synchronized automatically when process state changes. SQLite handles persistence with zero configuration.

## Contributing

<!-- TODO [Bill]: Write CONTRIBUTING.md and link it here.
     Recommend covering: dev setup, branch naming (feature/, bugfix/),
     commit conventions (feat:, fix:, chore:), testing requirements,
     and the Biome/Roslyn lint gates. -->

Contributions are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md) for development setup, coding conventions, and the pull request process.

## Credits

Collabhost is built by a human-AI collaborative team. The bots are autonomous AI agents on the [Collabot](https://github.com/MrBildo/collabot) platform --- they design, write code, review each other's work, and ship features alongside their human teammate.

**Bill Wheelock** -- Concept, design, and technical leadership -- [mrbildo@mrbildo.net](mailto:mrbildo@mrbildo.net)

**Bot Nolan** -- Project management -- [nolan@collabot.dev](mailto:nolan@collabot.dev)

**Bot Dana** -- Logo, *War Machine* theme, frontend design, TypeScript -- [dana@collabot.dev](mailto:dana@collabot.dev)

**Bot Remy** -- Backend design, architecture, C# -- [remy@collabot.dev](mailto:remy@collabot.dev)

**Bot Marcus** -- Backend design, architecture, C# -- [marcus@collabot.dev](mailto:marcus@collabot.dev)

**Bot Kai** -- Tooling, C# -- [kai@collabot.dev](mailto:kai@collabot.dev)

## License

[MIT](LICENSE)
