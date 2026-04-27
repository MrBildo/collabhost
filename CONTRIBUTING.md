# Contributing to Collabhost

Thanks for your interest in contributing. This guide covers everything you need to get a dev environment running and submit a clean PR.

> **Not trying to hack on the source?** End users install Collabhost via the one-line installer documented in the [README Install section](README.md#install). This guide is for building from source.

## Prerequisites

| Tool | Version | Install |
|------|---------|---------|
| .NET | 10+ | [dot.net](https://dot.net/download) |
| Node.js | 22+ | [nodejs.org](https://nodejs.org/) |
| Caddy | 2.x | [caddyserver.com](https://caddyserver.com/docs/install) (optional -- proxy features degrade gracefully without it) |

## Setup

Clone the repo and install frontend dependencies:

```bash
git clone https://github.com/MrBildo/collabhost.git
cd collabhost
cd frontend && npm install
```

No backend package restore step -- `dotnet build` handles that automatically.

### Caddy (optional)

Caddy is the reverse proxy layer. You can develop without it -- proxy features just won't be available.

**Local binary:** Download the Caddy binary to `tools/caddy/` and set the path in `appsettings.Development.json`:

```json
{
  "Proxy": {
    "BinaryPath": "<absolute-path-to-repo>/tools/caddy/caddy.exe"
  }
}
```

**Global install:** `winget install CaddyServer.Caddy` (Windows) or `sudo apt install caddy` (Linux). To point Collabhost at a system-installed Caddy, set `COLLABHOST_CADDY_PATH` to the absolute path before launching, or set `Proxy:BinaryPath` in `appsettings.Development.json`.

## Build and Run

### With Aspire (recommended)

```bash
dotnet run --project backend/Collabhost.AppHost
```

This starts the API, frontend dev server, and Aspire dashboard with OpenTelemetry. The dashboard URL is printed to the console on startup.

### Standalone

```bash
# Backend
dotnet run --project backend/Collabhost.Api

# Frontend (separate terminal)
cd frontend && npm run dev
```

The frontend dev server proxies API requests to the backend automatically. Open `http://localhost:5173` to use the dashboard.

### Initial admin key (dev)

On first run the backend seeds an administrator account and prints the API key to stdout. Copy it into the dashboard's API key prompt; it's stored in `localStorage` per browser.

To pin a specific key for dev work, add it to a gitignored `appsettings.Development.json`:

```json
{
  "Auth": {
    "AdminKey": "<your-ulid>"
  }
}
```

Generate a fresh ULID with:

```bash
dotnet run --file tools/generate-ids.cs
```

## Project Structure

```
collabhost/
├── backend/
│   ├── Collabhost.AppHost/        # Aspire orchestrator
│   ├── Collabhost.Api/            # Main API (registry, supervisor, proxy, MCP)
│   ├── Collabhost.Api.Tests/      # Integration tests (WebApplicationFactory + fakes)
│   ├── Collabhost.AppHost.Tests/  # Aspire smoke tests (real Kestrel + SQLite)
│   └── Collabhost.ServiceDefaults/
├── frontend/
│   └── src/
│       ├── actions/               # Action buttons and bars
│       ├── api/                   # Typed fetch client and endpoints
│       ├── chrome/                # Layout, topbar, auth gate
│       ├── forms/                 # Schema-driven form fields
│       ├── hooks/                 # TanStack Query hooks
│       ├── lib/                   # Shared utilities
│       ├── log/                   # Log viewer with ANSI rendering
│       ├── pages/                 # Route pages
│       ├── probes/                # Technology probe panels
│       ├── shared/                # Shared UI components
│       ├── status/                # Status dots, strips, stats
│       ├── styles/                # *War Machine* design tokens
│       ├── tables/                # Data tables and filters
│       └── test/                  # Vitest setup and shared test helpers
├── docs/
│   ├── install.sh                 # End-user installer (Linux / macOS)
│   └── install.ps1                # End-user installer (Windows)
└── tools/                         # Seed utilities and local dev helpers (e.g. generate-ids.cs)
```

## Testing

Run all tests before submitting a PR:

```bash
# Backend -- includes both integration and Aspire smoke tests
cd backend && dotnet test

# Frontend
cd frontend && npm run test
```

## Linting and Formatting

CI checks these. Save yourself a round trip by running them locally.

**Backend:**

```bash
cd backend
dotnet build Collabhost.slnx --no-incremental   # 0 errors, 0 warnings
dotnet format --verify-no-changes               # formatting clean
```

Use `--no-incremental` on the build so analyzer warnings surface -- incremental builds skip compilation and hide them.

**Frontend:**

```bash
cd frontend
npm run lint
npm run format:check
```

### Code style references

- **Backend:** `.editorconfig` at the repo root governs all C# formatting, naming, and analyzer severities. Don't override it -- configure your editor to respect it. Don't modify it to work around conflicts either -- restructure the code or use `#pragma` instead.
- **Frontend:** `biome.json` handles linting and formatting. No ESLint, no Prettier.

The internal team also maintains a coding-conventions document with project-specific overrides (kept local to each operator's machine, not part of the published source). If you're contributing regularly and want a preview of the internal conventions -- open an issue or ask in your PR and a maintainer will summarize the relevant section.

## Git Conventions

### Branches

- `feature/` -- new features
- `bugfix/` -- bug fixes
- `hotfix/` -- urgent production fixes

### Commits

We use [conventional commits](https://www.conventionalcommits.org/):

- `feat:` -- new feature
- `fix:` -- bug fix
- `chore:` -- maintenance, dependencies
- `docs:` -- documentation
- `refactor:` -- restructuring without behavior change

Write a short summary in the imperative mood: `feat: add health check polling` not `feat: added health check polling`.

### Merging

All PRs are squash-merged to `main`. Your commit history doesn't need to be perfect -- the squash merge cleans it up.

## Pull Requests

A good PR:

- **Has a clear title** using conventional commit format (`feat: add route table filtering`)
- **Describes what changed and why** -- not just "fixed stuff"
- **Includes a test plan** -- what you tested, how to verify
- **Passes CI** -- tests, lint, format checks all green
- **Stays focused** -- one concern per PR. Don't mix a feature with an unrelated refactor.

Reviewers look for: correctness, test coverage on changed paths, adherence to the code-style references above, and scope discipline (no unrelated drive-by changes). If your change touches the operator-facing surface (dashboard, installer, CLI output, MCP tool descriptions), expect a visual or UX review round -- attach screenshots where relevant.

If your change is large, consider opening a draft PR early to get directional feedback before investing in polish.

## Release pipeline dry-run

The real release workflow (`.github/workflows/publish.yml`) only runs when a tagged GitHub Release is published. That makes pipeline-only changes (RID matrix tweaks, Caddy version bumps, install-script changes, archive-contract changes) impossible to verify without cutting a real release.

`.github/workflows/publish-dryrun.yml` runs the same build matrix, frontend bundle, Caddy download, archive, and checksum steps -- and uploads the produced archives to the **workflow run** as a downloadable artifact. It never creates a tag and never touches the GitHub Releases surface.

**When it runs:**

- **On any PR** that touches `publish.yml`, `publish-dryrun.yml`, `docs/install.sh`, `docs/install.ps1`, `caddy.version`, or anything under `release-assets/`. The dry-run is a CI gate on those paths.
- **On demand** via `workflow_dispatch` -- run from the Actions tab. Optional `version` input (defaults to `0.0.0-dryrun`) stamps the produced archives.

**How to use it:**

1. Push your branch with the pipeline change.
2. Open a PR. If your diff matches the path filters, the dry-run runs automatically.
3. Or: navigate to **Actions -> Publish (dry-run) -> Run workflow**, pick your branch, optionally provide a version stamp.
4. Open the workflow run page. The `archive-<rid>` artifacts contain the produced archive + per-leg `.sha256`. The `checksums-aggregated` artifact contains the combined `checksums.txt`.
5. Download an archive, extract, and inspect. Same six-item contract as the real workflow.

**What it does not do:** create a tag, create or update a GitHub Release, upload to the Releases surface. If you see a `gh release` invocation in the dry-run, that's a bug -- file an issue.

## Questions?

Open an issue. We're a small project and happy to help contributors get oriented.
