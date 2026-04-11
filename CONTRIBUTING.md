# Contributing to Collabhost

Thanks for your interest in contributing. This guide covers everything you need to get a dev environment running and submit a clean PR.

## Prerequisites

| Tool | Version | Install |
|------|---------|---------|
| .NET | 10+ | [dot.net](https://dot.net/download) |
| Node.js | 22+ | [nodejs.org](https://nodejs.org/) |
| Caddy | 2.x | [caddyserver.com](https://caddyserver.com/docs/install) (optional -- proxy features degrade gracefully without it) |

## Setup

Clone the repo and install frontend dependencies:

```bash
git clone https://github.com/collabhost/collabhost.git
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

**Global install:** `winget install CaddyServer.Caddy` (Windows) or `sudo apt install caddy` (Linux). The default config resolves `caddy` from PATH.

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

## Testing

Run all tests before submitting a PR:

```bash
# Backend
cd backend && dotnet test

# Frontend
cd frontend && npm run test
```

## Linting and Formatting

The CI pipeline checks these. Save yourself a round trip by running them locally:

```bash
# Backend
cd backend && dotnet format --verify-no-changes

# Frontend
cd frontend && npm run lint
cd frontend && npm run format:check
```

### Code style references

- **Backend:** `.editorconfig` governs all C# formatting. Don't override it -- configure your editor to respect it.
- **Frontend:** `biome.json` handles linting and formatting. No ESLint, no Prettier.

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

If your change is large, consider opening a draft PR early to get directional feedback before investing in polish.

## Questions?

Open an issue. We're a small project and happy to help contributors get oriented.
