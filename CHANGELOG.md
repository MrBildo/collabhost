# Changelog

All notable changes to Collabhost are documented here. This project adheres to [Semantic Versioning](https://semver.org/).

## v1.10.0 — 2026-10-24

A security release. Every bundled runtime is updated, and the backend, the dashboard, the installers, the Linux and Windows services and the release pipeline are hardened. Update to pick up the fixes, and read the upgrade notes: some of them ask you to act.

### Security

- **Bundled .NET runtime updated from 10.0.10 to 10.0.13.** This rolls Microsoft's August, September and October .NET security updates: 60 CVEs, including two critical ASP.NET Core security feature bypasses (CVE-2026-88092, CVE-2026-70188) and several remote code execution fixes. Microsoft's [.NET 10 release notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/README.md) list them all.
- **Bundled Caddy updated from 2.11.4 to 2.11.7** (skipping 2.11.6, which broke long-lived streams), built with Go 1.26.10, which brings Go's own TLS and HTTP security fixes. The fixes from Caddy 2.11.5 and 2.11.6 that reach a Collabhost install:
  - connection-control headers from a hosted app could leak to clients on WebSocket upgrades ([CVE-2026-55458](https://github.com/caddyserver/caddy/security/advisories/GHSA-8qx7-j2v2-x6q5));
  - on Windows, files in a static site can no longer be reached through 8.3 short names in a folder name (the last part of the path was already covered).

  The same releases fix `forward_auth` ([GHSA-6365-7ppr-5r92](https://github.com/caddyserver/caddy/security/advisories/GHSA-6365-7ppr-5r92)), `path_regexp` (the last case of CVE-2026-52844) and `strip_prefix`/`handle_path`, which Collabhost doesn't configure.
- **The proxy also takes a newer HTTP/2 library** than Caddy 2.11.7 ships, closing five HTTP/2 flaws, including a crash any client could trigger.
- **SQLite updated** (SQLitePCLRaw 2.1.13), fixing CVE-2025-6965.
- **Also updated:** Microsoft.OpenApi 2.12.2 (CVE-2026-49451; Collabhost never parses OpenAPI documents, so this is for scanners).
- **The first-run admin key no longer goes to the logs.** Earlier releases wrote a generated admin key to every log Collabhost writes to: the Windows Application Event Log (readable by anyone who can sign in to the machine), the systemd journal, and any OpenTelemetry collector you had configured. It is now printed once on standard output (but not when standard output is the journal) and saved to `initial-admin-key.txt` in the data directory, mode 0600 on Linux and macOS. Delete the file once you've stored the key; Collabhost doesn't need it. See the upgrade notes.
- **Backend hardening:**
  - an admin key set in configuration is checked in constant time;
  - the `?key=` sign-in for the live log stream is now accepted only by that endpoint, not by any address that merely ends the same way;
  - Collabhost's own dashboard and API responses now forbid framing (`X-Frame-Options: DENY`, `frame-ancestors 'none'`), turn off content sniffing and send no referrer. Responses from your hosted apps are unchanged;
  - a new app's hostname must be a well-formed name that no other app, and not the dashboard, already uses;
  - external routes can't point at a link-local or cloud-metadata address, or at Google's metadata hostname, whatever `AllowPublicHosts` is set to. (With public hosts allowed, a DNS name that resolves to one of those addresses, or an IPv6 spelling of one such as NAT64, is not detected.)
- **Dashboard hardening:**
  - the dashboard's shipped packages have no known advisories (three remain in test-only tooling);
  - app names are encoded in every request the dashboard sends to the API, so a crafted name can't redirect a request to a different endpoint;
  - a hostile log line can no longer freeze the log viewer, and the viewer removes control characters and right-to-left override characters that could disguise log text;
  - fonts are served from Collabhost itself, so the dashboard makes no request to Google Fonts.
- **Windows system installs:** `%ProgramData%\Collabhost` is now readable and writable only by SYSTEM, Administrators and the Collabhost service. Earlier releases let every local user read it, including the configuration, the database (which holds every key) and the proxy's certificate authority key. If the installer finds a junction or symbolic link inside the folder, it stops before changing anything and tells you which one to delete. The system installer also takes back any file another user planted there.
- **Windows user-scope service installs:** the service account can now write only to its data folders and the proxy's certificate storage, so it can't replace the Collabhost programs. If you installed to a custom folder that every user can write to, the installer warns you; it doesn't change permissions on a folder you chose.
- **The Linux system service runs in a systemd sandbox.** It can't gain new privileges or use setuid, can't create namespaces, sees only basic devices (no GPUs, USB or serial adapters, or `/dev/kvm`), can't see other users' processes, can't change kernel settings, can't run 32-bit programs or open raw network sockets, and files it and its apps create are no longer readable by other users. Apart from its own private temporary folder, it can write only to `/var/lib/collabhost` and `/var/log/collabhost`. Hosted apps run inside the same sandbox.
- **Installers download over HTTPS only** (TLS 1.2 or newer). They refuse a plain `http://` base URL unless it points at this machine or `COLLABHOST_INSTALL_ALLOW_HTTP=1` is set. The PowerShell installers also check where a redirect finally landed.
- **Linux: secrets and file permissions.** Configuration examples now put secrets in a mode-0600 `EnvironmentFile=` instead of an inline `Environment=` line, which any local user can read with `systemctl show`. The user-scope installer writes its configuration 0600 and the data directory 0700 under any umask, keeps the default install location `~/.collabhost` unwritable by other users, and a re-run repairs loosened permissions. The user unit template sets `UMask=0077`.
- **Release archives carry signed build provenance.** INSTALL.md explains how to check a download with `gh attestation verify`. The release pipeline uses a write token only in a separate final job that checks out no code and runs no package manager, every GitHub Action it uses is pinned to an exact commit, and CI and release builds install frontend packages with install scripts disabled.

### Changed

- **Where to find the first-run admin key:** see Security above. The installers' closing message tells you where the key file is, and where older releases put the key if there's no file. Upgrading doesn't create the key file; your existing admin key keeps working.
- **App registration now refuses some input it used to accept.**
  - A custom domain pattern may use only letters, digits, dots, hyphens and the `{slug}` and `{baseDomain}` tokens, and must resolve to a valid hostname (otherwise 400). A hostname that another app or the dashboard already uses is refused with 409.
  - `serveMode` can't be set at registration, because Collabhost derives it. The settings API now reports `routing.serveMode` as `derived` instead of `locked`, and the dashboard shows it the same way.
  - External routes to link-local or cloud-metadata addresses are refused.
  - Existing apps keep running and aren't re-checked; a settings save that hits one of these rules is refused with a clear error.
- **Caddy 2.11.7 behaviour you may notice:**
  - requests whose headers total more than about 16 KiB are refused (431 on HTTP/1.1, a reset on HTTP/2), and Collabhost has no setting to raise it;
  - request header names containing `.` are no longer passed to hosted apps;
  - an upload or response that makes no progress for 60 seconds is cut. Pauses between writes, such as on a quiet event stream, don't count.
- **Agent keys:** the documentation and the New User screen now say plainly that an Agent key can run programs on the host (register any command as an app and start it) and browse its folders. Treat it like shell access to the machine.
- **Linux and macOS user scope:** an install location you don't own now stops the default install with a message; it used to go ahead.

### Upgrade notes

- **Replace your keys if they may have been read.** If Collabhost generated your admin key and anyone else could read the logs above, or if other people have accounts on the machine, replace every key: the admin key and every user key, including Agent and read-only keys, as described in INSTALL.md §2.1. Changing `Auth:AdminKey` alone does not revoke the old admin key. On Linux and macOS user-scope installs from earlier releases, the database may also have been readable by other local users who can enter your home folder. If you trusted Collabhost's local certificate authority on other devices and others could read it (for example, Windows system installs before this release), replace it and re-trust the new root on those devices, as described in INSTALL.md §9.11.4.
- **Check your external routes.** Routes registered earlier aren't re-checked. Look in Routes (or `GET /api/v1/routes`) for any upstream in `169.254.x.x` or `fe80::`, or a cloud-metadata address or `metadata.google.internal`, and remove it.
- **Move inline secrets.** If you put a secret (admin key, DNS provider token, telemetry API key) on an `Environment=` line in a unit or drop-in, any local user can read it with `systemctl show`. Move it to a file only you can read and use `EnvironmentFile=` (INSTALL.md shows the shape). If you run Collabhost from a copy of the user unit template, add `UMask=0077` to it.
- **Linux system installs:** fetch the current `install-system.sh` and run it, or use the one-line installer. A copy saved from an earlier release installs that release's unit, without the sandbox. Your own drop-ins still apply. If a hosted app needs something the sandbox blocks, INSTALL.md lists the one-line drop-in that relaxes each directive. A `Delegate=yes` drop-in now also needs `ProtectControlGroups=no`.
- **Linux and macOS user-scope installs:** if you installed an earlier release from a shell with `umask 000`, run `chmod go-w ~/.bashrc` (or `~/.zshrc` / `~/.profile`, whichever has Collabhost's PATH line), then re-run the installer.
- **Windows installs:** re-run `install-system.ps1`, or `install.ps1 -RegisterUserService` for a user-scope service, to correct folder permissions. A plain `install.ps1` run leaves the service's permissions as they were and says so. If the installer reports a junction or symbolic link, delete the link itself and re-run. If you moved the database, crash logs or the proxy's certificate storage outside Collabhost's own folders, give the service account Modify on the new location (INSTALL.md shows how). A database folder it can't write stops startup with "data directory not writable"; the certificate storage isn't checked at startup, so a missing grant shows up as the proxy reporting `degraded` on its first configuration load (or, if its certificate authority already exists, when it next issues or renews a certificate).

### Internal

- The workflow lint check now runs upstream `actionlint` pinned by checksum and fails on any finding. The wrapper it replaced reported findings without ever failing the check.
- The install tests check the first-run admin key file on every platform.
- When Collabhost runs from source in development mode, other websites can no longer call its API.

## v1.9.0 — 2026-08-10

Windows install hardening and parity, a read-only control-plane key tier, and stricter startup validation.

### Added

- **Windows system service now runs under a dedicated, least-privilege virtual account** instead of `LocalSystem`, tightening the system-scope security posture on par with the Linux service story.
- **`-RegisterUserService` install option** for Windows user-scope, service-managed installs — a native PowerShell service-registration path, no third-party tooling required.
- **Read-only / scoped control-plane key tier** — a key that observes the platform (system status, the app list, and app detail) without start/stop/kill, registration, or settings authority, and without the secret-bearing reads (logs, settings, activity events). Minted through the REST API and MCP.

### Changed

- **Registering an app now rejects unknown capability sections.** A registration request that names a capability section the app type does not support is refused with a clear error naming the section, instead of being silently accepted and stored. This is a deliberate clean break — requests that previously succeeded by quietly persisting an unsupported section will now fail and must be corrected. Settings updates are unaffected.
- **The configured admin key's shape is now validated at startup** — a misconfigured admin key fails loudly and early instead of being silently accepted.
- **Breaking:** the environment variable that overrides the reverse-proxy binary location is renamed from `COLLABHOST_CADDY_PATH` to `COLLABHOST_PROXY_BINARY_PATH`, aligning it with the other `COLLABHOST_PROXY_*` variables. The old name is no longer read — operators who set it must switch to the new name. The `Proxy:BinaryPath` setting in `appsettings.json` is unchanged.

### Fixed

- Alternate-ports deployments no longer have the automatic HTTPS `:80` redirect forced on them, so custom-port setups behave correctly.

### Internal

- A source-comment hygiene CI lint now blocks unresolvable internal references from entering published source, alongside shared-helper extraction and dead-config cleanup. No operator-facing behavior change.

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
