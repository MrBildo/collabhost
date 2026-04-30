<p align="center">
  <img src="frontend/public/favicon.svg" alt="Collabhost" width="80" />
</p>

<h1 align="center">Collabhost</h1>

<p align="center">
  A self-hosted control plane for your workstation — operated from a dashboard, or driven by agents through a built-in MCP server.<br/>
  Native process supervision on Windows and Linux. Runs on macOS with a fallback runner.
</p>

<p align="center">
  <a href="https://github.com/MrBildo/collabhost/actions/workflows/ci.yml"><img alt="CI" src="https://github.com/MrBildo/collabhost/actions/workflows/ci.yml/badge.svg?branch=main" /></a>
  <a href="LICENSE"><img alt="License: MIT" src="https://img.shields.io/badge/license-MIT-blue?style=flat-square" /></a>
  <a href="https://dot.net/download"><img alt=".NET 10" src="https://img.shields.io/badge/.NET-10-512BD4?style=flat-square" /></a>
  <a href="https://nodejs.org/"><img alt="Node 22+" src="https://img.shields.io/badge/node-22%2B-339933?style=flat-square" /></a>
</p>

---

## What is Collabhost?

Collabhost gives you a single control plane for everything running on your machine — .NET services, Node.js apps, static sites, MCP servers, and arbitrary executables. Register an app, point it at a directory, and Collabhost handles process supervision, reverse proxy routing, log aggregation, and crash recovery. No containers. No YAML. No cloud account.

**Two primary audiences.** An operator runs Collabhost from a War Machine dashboard — tables, log streams, inline actions. An agent runs Collabhost through a built-in MCP server — the same surface, exposed as tools over Streamable HTTP. Register an app from the UI or from Claude Code. Start, stop, tail logs, update settings. Humans and agents share one platform, one auth model, and one source of truth.

If you're building an AI harness, agent framework, or multi-agent system that needs to manage local infrastructure, Collabhost is the layer that sits underneath. See [For Agents](#for-agents) for MCP configuration.

It runs natively on **Windows** and **Linux** with platform-specific process management — no WSL required on Windows, no emulation layer on Linux. macOS runs the same control plane with a fallback process runner that has reduced containment — see [Platform support](#platform-support) for what differs. Think of it as a lightweight, self-hosted Heroku for your workstation — a control plane that stays out of your way until something goes wrong.

<p align="center">
  <img src="docs/screenshots/dashboard.png" alt="Collabhost Dashboard — stats, app table, and activity feed" width="900" />
</p>

<p align="center"><sub>The dashboard. Process supervision, routing, and a live activity feed on one screen.</sub></p>

## Install

One line. It downloads the latest release archive, verifies its SHA-256 against the release's `checksums.txt`, extracts to `~/.collabhost/bin` (Linux/macOS) or `%USERPROFILE%\.collabhost\bin` (Windows), and adds the directory to your `PATH`.

**Linux / macOS:**

```bash
curl -fsSL https://mrbildo.github.io/collabhost/install.sh | bash
```

Or download-then-execute if you'd rather inspect the script first:

```bash
curl -fsSL https://mrbildo.github.io/collabhost/install.sh -o install.sh
bash install.sh
```

**Windows (PowerShell):**

```powershell
iwr -useb https://mrbildo.github.io/collabhost/install.ps1 | iex
```

Or download-then-execute:

```powershell
iwr https://mrbildo.github.io/collabhost/install.ps1 -OutFile install.ps1
.\install.ps1
```

Then launch it:

```bash
collabhost
```

On first run, Collabhost seeds an administrator account and prints the API key to stdout:

```
[Collabhost] Admin key: 01JRSB8XH7D4Z2K9N0MFQPTVCW
```

Copy that key, open the dashboard at `http://localhost:58400`, and paste it into the API key prompt. You're in.

Re-running the installer is upgrade-safe: your `appsettings.json` and `data/` directory are preserved across versions. For full operator documentation — reinstall, upgrade, uninstall, configuration, troubleshooting — see the `INSTALL.md` shipped inside the release archive (or in your install directory after the first run).

### Caddy is bundled

The installer ships Caddy next to the Collabhost binary, and seeds `Proxy:BinaryPath` in `appsettings.json` to point at it on first install. No separate install step. On first launch, Collabhost starts the bundled Caddy as a supervised process and every registered app gets an automatic `{slug}.collab.internal` subdomain route.

If you'd rather use a system-installed Caddy, point Collabhost at it by either setting `COLLABHOST_CADDY_PATH` to the absolute path before launching (env var takes precedence) or editing `Proxy:BinaryPath` in `appsettings.json` directly. Operator edits to that key survive reinstalls — the installer only seeds the value when the key is absent or empty. See INSTALL.md §5.4 for the resolution chain. `BaseDomain` defaults to `collab.internal` — change it to use any domain you control.

If no Caddy binary is configured, everything else still works (app management, process supervision, logs, dashboard) — apps just don't get automatic subdomain routes, and `proxyState` reports `disabled` on `/api/v1/status`.

### Register your first app

**From the dashboard:** Open `http://localhost:58400` and click **Register App**. Pick an app type, point it at a directory, and hit create. Collabhost auto-discovers the start command and allocates a port. Click **Start** and watch the logs stream in.

**From an agent:** See [For Agents](#for-agents) below. An agent calls `list_app_types`, `browse_filesystem`, `detect_strategy`, `register_app`, and `start_app` — the same flow, scripted.

## Features

**Built-in MCP server** — A Model Context Protocol endpoint at `/mcp` exposes the operator surface as tools. 18 tools across discovery, lifecycle, configuration, registration, and activity. Agents register apps, start and stop processes, tail logs, update settings, and browse the host filesystem — programmatically, over Streamable HTTP. Role-aware: administrators see everything, agents see 16 of 18 tools (everything except `delete_app` and `list_events`). See [For Agents](#for-agents) for setup.

**Operator dashboard** — Real-time stats, app table with inline actions, live activity feed, and streaming log viewers. Everything an operator needs on one screen. The War Machine design system — dark, monospace, industrial — is built for density and quick action.

**Process supervision** — Start, stop, restart, and kill managed processes with platform-native implementations for both Windows and Linux. On Windows: processes launch via `CreateProcess` with dedicated process groups, graceful shutdown via `GenerateConsoleCtrlEvent`, and orphan protection through Win32 Job Objects that guarantee child process cleanup even if Collabhost crashes. On Linux: process groups with `SIGTERM`/`SIGKILL` lifecycle and cgroup-based containment. Crash detection with automatic restart and configurable exponential backoff. Stdout/stderr captured into in-memory ring buffers.

**Reverse proxy** — Every app gets a subdomain route automatically configured through [Caddy](https://caddyserver.com/). HTTPS via Caddy's internal CA. Routes sync on process state changes — no manual proxy config. The base domain is configurable in `appsettings.json`.

**Schema-driven configuration** — App settings are defined by capability schemas. New capabilities surface in the UI and through MCP without frontend or tool changes. Override defaults per-app, see what's customized vs. inherited.

**Multi-runtime support** — Five built-in app types out of the box:

| Type | What it runs |
|------|-------------|
| `dotnet-app` | .NET applications (auto-discovers project files) |
| `nodejs-app` | Node.js applications (reads package.json scripts) |
| `static-site` | Static file directories (served via Caddy file_server) |
| `executable` | Arbitrary binaries and scripts |
| `system-service` | Platform services managed by Collabhost itself |

**User management** — Header-based auth with administrator and agent roles. One-time API key reveal on creation. The same key authenticates the dashboard, the REST API, and the MCP server — mint a key for an agent and it can operate the platform.

**Technology probing** — Automatic detection of runtimes, frameworks, and dependencies for registered apps. No manual tagging required. Surfaces in the dashboard and in `get_app` MCP responses.

## Platform support

Process supervision is the piece of Collabhost that differs most by platform. The control plane itself — API, dashboard, MCP server, SQLite, Caddy integration — runs identically everywhere .NET 10 does. Process containment does not.

| Platform | How processes are supervised |
|---|---|
| **Windows** | `CreateProcess` P/Invoke with dedicated process groups, graceful shutdown via `GenerateConsoleCtrlEvent`, orphan protection through Win32 Job Objects. |
| **Linux** | `setsid` process groups with `SIGTERM`/`SIGKILL` lifecycle, cgroup v2 containment. Orphan-proof. |
| **macOS** | `FallbackProcessRunner`. Processes start and stop, stdout/stderr capture works, and hard kill is available. No graceful shutdown (no `SIGTERM`-equivalent signal handling), no Job Object-equivalent isolation, no orphan protection — if Collabhost crashes, child processes may outlive it. |

Windows and Linux are the recommended deployment targets. macOS runs the platform but with the gaps above — it's best suited for local development rather than long-running production workloads.

## A tour

<p align="center">
  <img src="docs/screenshots/apps.png" alt="App list with filter chips and search" width="900" />
</p>

<p align="center"><sub>App list. Filter by state, search, start and stop with one click.</sub></p>

<br/>

<p align="center">
  <img src="docs/screenshots/app-detail.png" alt="App detail with log viewer and route info" width="900" />
</p>

<p align="center"><sub>App detail. PID, port, uptime, route target, and live log streaming.</sub></p>

<br/>

<p align="center">
  <img src="docs/screenshots/app-detail-tech.png" alt="Technology tab with runtime and dependency detection" width="900" />
</p>

<p align="center"><sub>Technology probe. Collabhost detects runtimes, frameworks, and notable dependencies automatically.</sub></p>

<br/>

<p align="center">
  <img src="docs/screenshots/settings.png" alt="Schema-driven app settings" width="900" />
</p>

<p align="center"><sub>Schema-driven settings. Every capability surfaces here without frontend changes.</sub></p>

<br/>

<table>
  <tr>
    <td width="50%" align="center">
      <img src="docs/screenshots/register-type-picker.png" alt="App type picker" />
      <br/>
      <sub>Register, step 1. Pick a type.</sub>
    </td>
    <td width="50%" align="center">
      <img src="docs/screenshots/register-form.png" alt="Schema-driven registration form" />
      <br/>
      <sub>Register, step 2. Configure.</sub>
    </td>
  </tr>
</table>

<br/>

<p align="center">
  <img src="docs/screenshots/routes.png" alt="Routes table showing Caddy proxy configuration" width="900" />
</p>

<p align="center"><sub>Routes. Every app gets an automatic <code>{slug}.collab.internal</code> subdomain with HTTPS (the base domain is configurable).</sub></p>

## For Agents

Collabhost exposes an MCP (Model Context Protocol) server so agents can operate the platform directly — no custom HTTP client, no REST adapter. If your agent speaks MCP, it speaks Collabhost.

### Endpoint

| | |
|---|---|
| URL | `http://localhost:58400/mcp` |
| Transport | Streamable HTTP (stateless) |
| Auth | `X-User-Key` header with a user's ULID key |
| Server name | `collabhost` |

The API port defaults to `58400`. If something else on your host already owns that port, override it via `Hosting:ListenPort` in `appsettings.json` or the `COLLABHOST_HOSTING_LISTEN_PORT` environment variable.

### Configure an agent client

Claude Code, and any other client that reads project-scoped `.mcp.json`, connects with this config:

```json
{
  "mcpServers": {
    "collabhost": {
      "type": "http",
      "url": "http://localhost:58400/mcp",
      "headers": {
        "X-User-Key": "<your-agent-key>"
      }
    }
  }
}
```

Drop that in your project's `.mcp.json` (or the equivalent for your client) and your agent has Collabhost as a tool surface. Other MCP-speaking clients typically accept the same three pieces of information in their own configuration format: transport type (`http`), endpoint URL, and the `X-User-Key` header.

### Mint an agent key

1. Sign in to the dashboard as an administrator.
2. Open **Users** from the topbar.
3. Click **Create User**, pick the **Agent** role, give it a name, and create.
4. The key is revealed **once** on creation. Copy it into your MCP config. If you lose it, deactivate the user and mint a new one.

### Roles

| Role | Access |
|------|--------|
| Administrator | Full tool surface (18 tools) plus user management through the REST API. |
| Agent | 17 of 18 tools. Everything except `delete_app` (deletion is administrator-only). |

### Tool surface

18 tools, grouped by workflow:

- **Discovery (4)** — `get_system_status`, `list_apps`, `get_app`, `list_app_types`. The agent's starting point: what's on the platform, what's running, what can it register.
- **Lifecycle (5)** — `start_app`, `stop_app`, `restart_app`, `kill_app`, `get_logs`. Full process control. `get_logs` is token-budgeted for LLM context.
- **Configuration (4)** — `get_settings`, `update_settings`, `list_routes`, `reload_proxy`. Read and change schema-driven settings, inspect Caddy routes.
- **Registration (4)** — `register_app`, `delete_app`, `browse_filesystem`, `detect_strategy`. End-to-end app setup. `browse_filesystem` lets agents locate install directories iteratively; `detect_strategy` reports what Collabhost can auto-discover for a given path and app type.
- **Activity (1)** — `list_events`. Recent state changes and operator actions, filterable by app, event type, or category.

Each tool has a full description, parameter schema, and read-only/destructive/idempotent annotations. The MCP server also ships `ServerInstructions` describing common workflows (registration, lifecycle, diagnostics) so a freshly-connected agent has a usable mental model without reading the source.

Apps are identified by **slug** throughout (e.g. `my-api-server`), not by ULID. Agents use the same identifier operators see in the URL bar.

## Tech Stack

| Layer | Technology |
|-------|-----------|
| Backend | .NET 10, C# Minimal API, EF Core, SQLite |
| Frontend | React 19, TypeScript, Vite |
| Design System | *War Machine* (custom — dark, monospace, industrial) |
| Reverse Proxy | Caddy (managed via JSON admin API) |
| Orchestration | .NET Aspire, OpenTelemetry |
| Testing | xUnit + Shouldly (backend), Vitest (frontend) |
| Linting | Roslyn analyzers (backend), Biome (frontend) |

## Architecture

Collabhost has four layers:

- **Caddy** is the front door — edge reverse proxy, TLS termination, routing
- **ASP.NET Core** is the control tower — app registry, process supervision, auth, and the MCP endpoint
- **React dashboard** is the operator console — the *War Machine* design system
- **MCP server** is the agent console — the same operator surface, exposed as tools at `/mcp`

Apps are registered with a slug, discovered from the filesystem, and supervised as managed processes. Caddy routes are synchronized automatically when process state changes. SQLite handles persistence with zero configuration. The REST API and MCP endpoint are parallel presentation surfaces over the same shared services — anything an operator can do from the dashboard, an agent can do from an MCP client.

## Contributing

Contributions are welcome. If you'd like to build from source, run the dev environment, or submit a pull request, see [CONTRIBUTING.md](CONTRIBUTING.md) for prerequisites, setup, coding conventions, and the PR process.

## Credits

Collabhost is built by a human-AI collaborative team. The bots are autonomous AI agents on the Collabot platform — they design, write code, review each other's work, and ship features alongside their human teammate.

**Bill Wheelock** — Concept, design, and technical leadership — [mrbildo@mrbildo.net](mailto:mrbildo@mrbildo.net)

**Bot Nolan** — Project management — [nolan@collabot.dev](mailto:nolan@collabot.dev)

**Bot Dana** — Logo, *War Machine* theme, frontend design, TypeScript — [dana@collabot.dev](mailto:dana@collabot.dev)

**Bot Remy** — Backend design, architecture, C# — [remy@collabot.dev](mailto:remy@collabot.dev)

**Bot Marcus** — Backend design, architecture, C# — [marcus@collabot.dev](mailto:marcus@collabot.dev)

**Bot Kai** — Tooling, C# — [kai@collabot.dev](mailto:kai@collabot.dev)

## License

[MIT](LICENSE)
