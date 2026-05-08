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

Caddy is the reverse proxy layer. You can develop without it -- proxy features just won't be available, and `proxyState` will report `disabled` on `/api/v1/status`.

The shipped installer (`docs/install.ps1` / `docs/install.sh`) seeds `Proxy:BinaryPath` in `appsettings.json` for end users. The `dotnet run` dev workflow does **not** go through the installer, so dev contributors set `Proxy:BinaryPath` once in a gitignored `appsettings.Development.json`:

```json
{
  "Proxy": {
    "BinaryPath": "<absolute-path-to-repo>/tools/caddy/caddy.exe"
  }
}
```

The resolver reads env var > appsettings > null. `COLLABHOST_CADDY_PATH` (env var) is the alternative if you'd rather not edit a settings file.

**Getting a Caddy binary:** download from [caddyserver.com](https://caddyserver.com/docs/install) into `tools/caddy/`, or install globally with `winget install CaddyServer.Caddy` (Windows) / `sudo apt install caddy` (Linux). For a global install, point `Proxy:BinaryPath` (or `COLLABHOST_CADDY_PATH`) at the absolute path of the system binary.

**Vanilla vs plugin-baked Caddy.** A vanilla Caddy is fine for everyday dev — the proxy defaults to Caddy's internal CA, which doesn't depend on any DNS plugin. The CI release pipeline produces a Caddy with `caddy-dns/cloudflare` (and any other plugins listed in `caddy-plugins.txt`) baked in via `xcaddy`; that binary is what end users get. If you're locally exercising the ACME branch (`Proxy:DnsProvider` set), you need the plugin-baked binary too. Reproduce the CI build locally with `tools/build-caddy.ps1` (Windows) or `tools/build-caddy.sh` (Linux/macOS); both read the same pin files (`caddy.version`, `xcaddy.version`, `caddy-plugins.txt`) the CI workflow does. Requires [Go](https://go.dev/dl/) on PATH.

#### TLS issuer: internal CA vs ACME (Let's Encrypt)

By default the proxy uses Caddy's internal CA (`Proxy:DnsProvider` is unset / empty). This is the right choice for `*.collab.internal`-style local deployments — certificates are signed by Caddy's local authority and the operator trusts the root manually.

For real internet-reachable domains, set `Proxy:DnsProvider` (e.g. `"cloudflare"`) and the proxy emits a Let's Encrypt issuer block configured for DNS-01 challenge. The DNS API token is read from the host process env at Caddy spawn time — its name comes from `Proxy:DnsApiTokenEnvVar` (defaults to `CLOUDFLARE_API_TOKEN`). The token never touches the database, the JSON config snapshot, or any log line.

| Setting | Env var | Default | Notes |
|---|---|---|---|
| `Proxy:DnsProvider` | `COLLABHOST_PROXY_DNS_PROVIDER` | unset (internal CA) | Caddy DNS provider name. Currently only `cloudflare` is built into the shipped Caddy. |
| `Proxy:DnsApiTokenEnvVar` | `COLLABHOST_PROXY_DNS_API_TOKEN_ENV_VAR` | `CLOUDFLARE_API_TOKEN` | Name of the host-process env var carrying the DNS API token. |

The shipped Caddy build must include the corresponding `caddy-dns/<provider>` plugin for ACME issuance to work — see `docs/release-process.md`.

**Where to put the DNS API token.** Two paths reach Caddy and both work:

- **Dashboard env-vars editor (recommended).** Open the proxy app's settings page and set `CLOUDFLARE_API_TOKEN` (or whichever name `Proxy:DnsApiTokenEnvVar` resolves to) under **Environment Variables**. Persisted in the Collabhost database; survives upgrades. This is the same mechanism every other managed app uses for its env vars.
- **Host process env (legacy).** Place the value in Collabhost's host environment — e.g. on Linux/systemd, a drop-in at `~/.config/systemd/user/collabhost.service.d/cloudflare-token.conf` containing `Environment=CLOUDFLARE_API_TOKEN=<value>`. The proxy's `IProcessEnvironmentProvider` reads it from the host env and contributes it to Caddy's child process at spawn time.

Both routes coexist by design. **Host-env contributions win on key conflict** with dashboard values — the platform-internal provider is the source of truth for any key it contributes. If you migrate from a host-env drop-in to the dashboard, remove the drop-in and reload (`systemctl --user daemon-reload && systemctl --user restart collabhost`) so the dashboard value is the only source.

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

**Dev vs prod URLs.** During `aspire start`, the React dashboard is served by Vite at `http://localhost:5173` with HMR; `http://localhost:58400` is the API only and will return 404 (or fall through to auth → 401) for `/`. In a published binary (post-`dotnet publish` or installer), the dashboard ships in `wwwroot/` and is served by the API at `http://localhost:58400`. The `/api/v1/*` paths are the same in both modes; only the static-asset-serving differs.

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

## Install integration test

`.github/workflows/install-integration.yml` is the consume-side complement to the dry-run. The dry-run validates that archive **builds** are correct; this workflow validates that the published install scripts (`docs/install.sh`, `docs/install.ps1`) actually consume a real GitHub Release end-to-end and that the resulting binary works as expected.

**When it runs:**

- **On any PR** that touches `docs/install.sh`, `docs/install.ps1`, `publish.yml`, `publish-dryrun.yml`, `install-integration.yml`, or `release-assets/`. Failures block merge for those PRs.
- **On `release.published`** -- post-release validation that the archives we just shipped install correctly across all RIDs.
- **On demand** via `workflow_dispatch` with an optional `version` input (a release tag like `v0.1.0`).

**Release-process checklist (before tagging):**

- [ ] Trigger `install-integration.yml` via `workflow_dispatch` and confirm the `linux-arm64` leg passes (the QEMU leg only runs on manual + `release.published`, not PRs).

**What each matrix leg verifies (per RID):**

1. The install script succeeds against the live GitHub Release.
2. `collabhost --version` runs from `$HOME` (catches CWD-relative `ContentRootPath` regressions).
3. A reinstall preserves an operator-edited `appsettings.json` and a populated `data/` directory.
4. The bundled Caddy version is reported (and on `release.published` runs, must match the `caddy.version` pin on the released commit).
5. The first-boot admin-key bootstrap line (`Collabhost admin key:`) emits on stdout.

**RID matrix:**

| RID | Runner |
|---|---|
| `linux-x64` | `ubuntu-latest` |
| `linux-arm64` | `ubuntu-latest` + Docker QEMU (`linux/arm64`) |
| `osx-arm64` | `macos-latest` |
| `win-x64` | `windows-latest` |

**linux-arm64 caveat:** GitHub does not provide a hosted ARM64 Linux runner, so the leg runs inside a `linux/arm64` Debian container under QEMU emulation on `ubuntu-latest`. Emulation is 5-15x slower than native, so the leg has a 45-minute timeout; it is allowed to lag the native legs without blocking them (`fail-fast: false`).

**Intel Mac (osx-x64) not supported:** The `osx-x64` archive was dropped in v0.1.1. GitHub wound down `macos-13` Intel runners; queue starvation made CI coverage impossible. macOS on Apple Silicon (`osx-arm64`) is the supported macOS platform.

**On a PR run, the target version is the latest published release** -- there may not be an unreleased tag to test against. On a `release.published` run, the target is the just-shipped tag.

## Maintaining releases

If you're cutting a Collabhost release (tagging, publishing the archive, bumping bundled deps), see [docs/release-process.md](docs/release-process.md). It covers the bundled-Caddy CVE response process, the response-time SLA, the pre-release checklist, and the version-pin mechanism (`caddy.version`). Bundling Caddy means Collabhost owns its CVE response window for end users -- the process exists so security fixes don't get stuck behind us.

## Questions?

Open an issue. We're a small project and happy to help contributors get oriented.
