# Release Process

This doc covers the operational obligations Collabhost takes on by bundling third-party binaries — primarily Caddy. If you're a contributor working on a regular feature PR, you don't need this doc; see [CONTRIBUTING.md](../CONTRIBUTING.md). This doc is for maintainers cutting a release.

## Cutting a release

Collabhost releases are **tag-then-release**. The Publish workflow is triggered by a published GitHub Release, not by a pushed tag — so the order is: create an annotated tag, push it, then create the Release against that tag.

### The flow

```bash
# 1. Annotated tag on the exact commit you are releasing (use the FULL sha).
git tag -a vX.Y.Z <full-sha> -m "Collabhost vX.Y.Z"

# 2. Push the tag.
git push origin vX.Y.Z

# 3. Create the GitHub Release against the now-pushed tag.
gh release create vX.Y.Z \
  --verify-tag \
  --title "Collabhost vX.Y.Z" \
  --notes "<operator-facing release notes>"
```

`--verify-tag` makes `gh release create` fail rather than silently create a tag if the tag is missing — it forces the tag-first order and catches a typo'd tag name early.

### The `--target` short-SHA gotcha

`gh release create --target <short-sha>` is **rejected** by the GitHub API with `Release.target_commitish is invalid`. `--target` accepts only a branch name or a **full** 40-char commit SHA — never an abbreviated SHA. The tag-first flow above sidesteps this entirely: once the tag exists and is pushed, the Release resolves the commit from the tag and `--target` is not needed at all. If you must use `--target` (e.g. creating the tag at release time), pass a branch name or the full SHA.

### What publishing triggers

`.github/workflows/publish.yml` triggers on `release: types: [published]`. On a published Release it:

- Parses the tag (must match `vX.Y.Z` or `vX.Y.Z-<pre-release>`, SemVer 2.0 §9; build metadata `+...` is rejected).
- Builds the frontend once and runs the per-RID build matrix (`linux-x64`, `linux-arm64`, `osx-arm64`, `win-x64`).
- Stages the **seven-item archive contract** per RID (collabhost binary, caddy binary, `appsettings.json`, `INSTALL.md`, `LICENSES/caddy-LICENSE`, `LICENSES/caddy-NOTICE`, `wwwroot/`) into a flat layout, archives it, and computes a per-leg `.sha256`.
- Uploads `collabhost-<ver>-<rid>.<ext>` + its `.sha256` to the Release as assets (`--clobber`), then aggregates all per-leg sums into a single `checksums.txt` uploaded to the Release.

### Post-release validation

`install-integration.yml` fires **automatically via `workflow_run`** when `publish.yml` completes successfully — it downloads the just-shipped Release archives and exercises the live install scripts across all RIDs. This is a post-release validation, **not a PR gate** and **not a `release.published` trigger** (the `workflow_run` chaining replaced `release.published` per card #211 to avoid a parallel-fire race). Watch this run after publishing; a green Publish does not by itself prove the install scripts consume the archives correctly. PR-time archive smoke is a separate concern handled by `publish-dryrun.yml`'s `archive-smoke` job (card #184).

## Bundled-binary CVE response

Collabhost ships a Caddy binary alongside its own binary in every release archive. End users do not install Caddy separately — the installer extracts ours. **Bundling means we own Caddy's CVE response window for our users**: when upstream Caddy ships a security release, Collabhost must ship a patch release with the updated binary or end-user installs are stuck on the vulnerable version.

This section captures the process. Automation (Renovate, Dependabot) is future scope; today the discipline is human + checklist.

### Monitoring

We watch upstream Caddy for security releases via:

- **GitHub Security Advisories** — `https://github.com/caddyserver/caddy/security/advisories`. The maintainer feed for actual CVE disclosures. Subscribe via your GitHub notification settings (Watch → Custom → Security alerts on `caddyserver/caddy`).
- **GitHub release watch** — `https://github.com/caddyserver/caddy/releases`. Watches every release, security or otherwise. Useful as a backstop in case an advisory wasn't filed alongside the patch.
- **Manual pre-release check** — every Collabhost release cuts after a fresh look at the two sources above. Item 1 of the release checklist below.

If you maintain Collabhost releases, you are responsible for being subscribed to at least one of the first two. The pre-release check is a backstop, not the primary signal.

### Response SLA

| Severity | Target window from upstream patch → Collabhost patch release |
|---|---|
| **Critical** (CVSS 9.0+, RCE, auth bypass) | 3 business days |
| **High** (CVSS 7.0–8.9) | 7 business days |
| **Medium / Low** | Next regular release |
| **Informational** | Best-effort; rolled into next release |

These are targets, not contracts. Collabhost is a small project. If a critical advisory lands and the maintainer isn't available, the project ships when the maintainer ships. Pinning a vulnerable version with no patch path is the failure mode this SLA is meant to prevent — not a guarantee of latency.

### Release checklist (every release, not just security)

Before tagging a release, walk this list:

- [ ] **Caddy upstream check.** Is there a newer Caddy release than the one pinned in `caddy.version`? Read its release notes and security advisories. If a CVE is fixed, treat the bump as required and surface it in the Collabhost release notes.
- [ ] **Bump `caddy.version` if needed.** Single-line file at the repo root. The publish workflow feeds this version to `xcaddy build` for each target RID.
- [ ] **Plugin upstream check.** For every line in `caddy-plugins.txt`, verify the pinned version is still current at the plugin's upstream repo (e.g. `github.com/caddy-dns/cloudflare`). Bump if a security fix or compatibility-with-new-Caddy-core release lands.
- [ ] **Smoke-test the new Caddy version against `CaddyClient`.** Collabhost talks to Caddy through the admin API (`POST /load`, `PATCH /config/...`). Caddy occasionally tightens admin-API behaviour between minors. Run `aspire start` or a standalone backend with the bumped version, register a managed app, exercise route reload. Log lines should be clean.
- [ ] **Run the dry-run pipeline.** `.github/workflows/publish-dryrun.yml` builds the same archives the real release will produce. Required if `caddy.version`, `xcaddy.version`, or `caddy-plugins.txt` changed. The post-build step asserts every plugin's Caddy module is reachable via `caddy list-modules` — a missing plugin fails the leg, so a bump that breaks plugin compatibility surfaces here, not in production.
- [ ] **Run the install-integration workflow.** Trigger `install-integration.yml` via `workflow_dispatch` and confirm at minimum the `linux-arm64` leg passes (the QEMU leg only runs on manual dispatch and post-release `workflow_run`).
- [ ] **Run the release UAT runbook.** See [`docs/release-uat.md`](release-uat.md). Create a Collaboard card for the UAT run before starting (lane: Triage; title: `UAT v<version> — pending`); link it from the release notes as `UAT card: <card-link>`. The UAT gate is **advisory** (no CI enforcement) — the linked card in the release notes is the visibility mechanism that makes the gate have teeth. See `docs/release-uat.md` § "How to use this runbook" for the protocol.
- [ ] **Credit upstream in release notes.** When a release rolls a Caddy security fix, note the CVE ID(s) and link the upstream advisory. Don't quietly ship security fixes — give operators a reason to update.

### Version pin mechanism

Three pin files at the repo root drive the bundled Caddy build, all consumed by the publish workflow (`.github/workflows/publish.yml`):

| File | Shape | Purpose |
|---|---|---|
| `caddy.version` | Single-line plain text (e.g. `2.11.2`) | Caddy core version. Fed to `xcaddy build vX.Y.Z`. |
| `xcaddy.version` | Single-line plain text (e.g. `0.4.5`) | Pinned `xcaddy` itself, installed via `go install` at build time. Bumping is rare — only when xcaddy ships a build-side fix. |
| `caddy-plugins.txt` | One plugin per non-comment line: `<go-module-path>  <version>  <caddy-module-id>` | Plugins baked into the build. The third column (`caddy-module-id`) is what `caddy list-modules` prints once the plugin is loaded; the workflow grep-asserts it post-build. |

**Bumping Caddy** now means a coordinated walk: bump `caddy.version`, then check whether each plugin in `caddy-plugins.txt` has a newer release tag compatible with the new Caddy core. The dry-run pipeline catches mismatches at build time (Go's module resolution refuses incompatible combinations) and at verify time (a plugin that loaded but moved its Caddy module ID will fail the assertion).

**Adding a plugin:** append a line to `caddy-plugins.txt` with the three columns. The CI build step assembles `--with` flags from the file; no workflow edit needed. Update [CONTRIBUTING.md](../CONTRIBUTING.md) if the plugin enables a new operator-facing setting.

**Local reproduction.** `tools/build-caddy.ps1` (Windows) and `tools/build-caddy.sh` (Linux/macOS) read the same three pin files and produce a binary structurally identical to the CI build. Requires [Go](https://go.dev/dl/) on PATH. Useful for verifying a bump locally before pushing.

### Future scope

Out of scope for this doc, tracked separately:

- **Renovate / Dependabot automation.** Auto-PR a `caddy.version` bump on upstream release.
- **CI gate on Caddy bump PRs.** A smoke-test workflow that runs `CaddyClient` against the proposed version before merge.
- **GitHub Security Advisory automation.** Subscribe Collabhost to advisories that land specifically on `caddyserver/caddy` and surface them in the project's own advisory feed.

If you're picking these up, file a card and link it back to this section.

## See also

- [CONTRIBUTING.md § Release pipeline dry-run](../CONTRIBUTING.md#release-pipeline-dry-run) — how to verify pipeline-only changes without cutting a real release.
- [CONTRIBUTING.md § Install integration test](../CONTRIBUTING.md#install-integration-test) — consume-side validation of the install scripts against published archives.
