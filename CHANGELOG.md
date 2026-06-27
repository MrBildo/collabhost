# Changelog

All notable changes to Collabhost are documented here. This project adheres to [Semantic Versioning](https://semver.org/).

## Unreleased

### Changed

- **Registering an app now rejects unknown capability sections.** A registration request that names a capability section the app type does not support is refused with a clear error naming the section, instead of being silently accepted and stored. This is a deliberate clean break — requests that previously succeeded by quietly persisting an unsupported section will now fail and must be corrected. Settings updates are unaffected.
- **Breaking:** the environment variable that overrides the reverse-proxy binary location is renamed from `COLLABHOST_CADDY_PATH` to `COLLABHOST_PROXY_BINARY_PATH`, aligning it with the other `COLLABHOST_PROXY_*` variables. The old name is no longer read — operators who set it must switch to the new name. The `Proxy:BinaryPath` setting in `appsettings.json` is unchanged.

## v1.8.0 — 2026-06-24

A hardening release: a broad pass of reliability, correctness, and security fixes, plus internal-quality and toolchain work. It also rolls a bundled-Caddy security update.

### Security

- **Bundled Caddy updated to 2.11.4**, rolling upstream security fixes — most notably a HIGH-severity Windows `file_server` path-authorization bypass (CVE-2026-52844 / GHSA-qrp7-cvwr-j2c6) that affects static-site serving on Windows, plus FastCGI and admin-API authorization-bypass fixes. Because Collabhost ships Caddy in its release archive, operators get the patched binary by updating Collabhost. Advisories rolled: GHSA-qrp7-cvwr-j2c6, GHSA-f59h-q822-g45g, GHSA-m675-2p33-xv9g, GHSA-x5w9-xh9r-mvfc, GHSA-gx7w-56w6-g48x, GHSA-vcc4-2c75-vc9v.
- Destructive control-plane operations (start, stop, kill, delete, register, settings) now require an Administrator role; read-only callers can observe but not mutate.
- Tightened auth-skip path matching so look-alike paths can no longer slip past the auth wall.
- Remediated all known frontend dependency vulnerabilities.

### Added

- A boot-time warning when a runtime-config overlay route is active but its file is missing.

### Changed

- Route-target labels are now consistent and vendor-neutral across the dashboard and the MCP tools: static-site routes read "Static Files" everywhere, and the reverse-proxy vendor name no longer appears in any operator-facing status or error string. Note: the MCP `get_app` / `list_routes` `target` field for static-site routes now reads `Static Files` instead of `file-server`.

### Fixed

- **Reliability and correctness:** pre-migration backup now captures commits still in the SQLite write-ahead log, preventing data loss on upgrade; the activity-events feed no longer errors past the first page; system uptime is now trustworthy; app detail no longer reports inconsistent state (for example "running" with a null PID); app lifecycle operations are serialized and process state is locked; deleting an app fully reclaims its cached resources and activity history is bounded; the reverse proxy recovers correctly after a failure, with a bounded admin-API timeout and a corrected bootstrap path; the live log stream sends a real keepalive so idle-but-live streams are not dropped; health probes run independently so one slow endpoint no longer delays the others; on Windows, stopping an app is now an honest immediate stop with no misleading delay.
- **Operator experience:** authentication failures are surfaced clearly instead of silently returning to the login screen; data feeds stay resilient on transient errors instead of freezing; form fixes for clearing a field, renaming a key without destroying a sibling row, and surfacing failed actions; the log viewer is hardened; the app list shows the correct URL scheme; the Routes table shows proxy health; deleting an app no longer logs a 404; and there are accessibility and per-row action-feedback improvements.

### Internal

- Major internal restructuring for long-term maintainability — endpoint, operation, and process-supervisor decomposition, with architecture tests enforcing the structure. No operator-facing behavior change.
- Toolchain modernization: a pinned .NET SDK, Central Package Management, Aspire 13.4 (stable), and migration to xUnit v3 / Microsoft Testing Platform.

### Docs

- Operator documentation reconciled against reality (the built-in app-type count, the data-path description, internal-service notes), credits updated, and internal references scrubbed from published docs. A pre-release documentation sweep is now a standing step in the release process.
