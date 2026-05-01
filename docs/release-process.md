# Release Process

This doc covers the operational obligations Collabhost takes on by bundling third-party binaries — primarily Caddy. If you're a contributor working on a regular feature PR, you don't need this doc; see [CONTRIBUTING.md](../CONTRIBUTING.md). This doc is for maintainers cutting a release.

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
- [ ] **Bump `caddy.version` if needed.** Single-line file at the repo root. The publish workflow downloads this version into the release archive.
- [ ] **Smoke-test the new Caddy version against `CaddyClient`.** Collabhost talks to Caddy through the admin API (`POST /load`, `PATCH /config/...`). Caddy occasionally tightens admin-API behaviour between minors. Run `aspire start` or a standalone backend with the bumped version, register a managed app, exercise route reload. Log lines should be clean.
- [ ] **Run the dry-run pipeline.** `.github/workflows/publish-dryrun.yml` builds the same archives the real release will produce. Required if `caddy.version` changed.
- [ ] **Run the install-integration workflow.** Trigger `install-integration.yml` via `workflow_dispatch` and confirm at minimum the `linux-arm64` leg passes (the QEMU leg only runs on manual + `release.published`).
- [ ] **Credit upstream in release notes.** When a release rolls a Caddy security fix, note the CVE ID(s) and link the upstream advisory. Don't quietly ship security fixes — give operators a reason to update.

### Version pin mechanism

The pinned Caddy version lives in `caddy.version` at the repo root — a single-line file containing the version number (e.g. `2.11.2`). The publish workflow (`.github/workflows/publish.yml`) reads this file at archive-build time and downloads the matching binary from the official Caddy release surface for each target RID.

When you bump it, that's the only file you change for a Caddy version bump (apart from the release notes).

### Future scope

Out of scope for this doc, tracked separately:

- **Renovate / Dependabot automation.** Auto-PR a `caddy.version` bump on upstream release.
- **CI gate on Caddy bump PRs.** A smoke-test workflow that runs `CaddyClient` against the proposed version before merge.
- **GitHub Security Advisory automation.** Subscribe Collabhost to advisories that land specifically on `caddyserver/caddy` and surface them in the project's own advisory feed.

If you're picking these up, file a card and link it back to this section.

## See also

- [CONTRIBUTING.md § Release pipeline dry-run](../CONTRIBUTING.md#release-pipeline-dry-run) — how to verify pipeline-only changes without cutting a real release.
- [CONTRIBUTING.md § Install integration test](../CONTRIBUTING.md#install-integration-test) — consume-side validation of the install scripts against published archives.
