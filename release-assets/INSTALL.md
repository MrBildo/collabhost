# Collabhost — Install

A self-hosted application platform for services running on your own machine.
This guide walks you from extracted archive to logged-in dashboard.

**Supported platforms:** Linux (x64, arm64), macOS arm64 (Apple Silicon), Windows (x64).
win-arm64 and macOS x64 (Intel) are not supported.

---

## 1. Quick start

If you used the install script (`install.sh` or `install.ps1`), Collabhost is
already extracted to `$HOME/.collabhost/bin` and added to your `PATH`.

If you downloaded and extracted the archive manually, `cd` into
`collabhost-<version>-<rid>/` first.

```sh
# Linux / macOS
./collabhost
```

```powershell
# Windows
.\collabhost.exe
```

On first launch, Collabhost:

1. Creates `data/` and `data/backups/` next to the binary (0700 on POSIX).
2. Creates `collabhost.db` and applies all migrations.
3. Seeds the admin user and **prints the admin key to stdout**.
4. Starts the bundled Caddy reverse proxy on `:80` and `:443` (the default
   `Proxy:ListenAddress` is `:80,:443` — Caddy auto-redirects HTTP to HTTPS).
5. Listens on `http://localhost:58400` for the dashboard.

Open `http://localhost:58400` in your browser. See §2 to capture the admin key
and §3 for what a healthy first load looks like. If you are reaching the
dashboard from a different device on your LAN (laptop, phone, tablet),
see §9.10 for the four-layer diagnostic walkthrough that covers privileged
ports, DNS, the host firewall, and CA trust.

---

## 2. Your admin key

On the **very first launch** against an empty database, Collabhost seeds an
admin user and emits the full authentication key (a ULID) as a critical-level
log line exactly once. Two lines emit back-to-back:

```
info: Collabhost.Api.Authorization.UserSeedService[0]
      Admin user seeded. Key: 01JABCDE...
crit: Collabhost.Api.Authorization.UserSeedService[0]
      Collabhost admin key: 01JABCDEFGHJKMNPQRSTVWXYZ
```

The `info` line carries a truncated 8-character hint — it confirms that seeding
ran but is not the full key. The `crit` line carries the full ULID; that is
the value you need. The `crit:` prefix renders with distinct color/intensity in
most terminals, so it stands out in scrollback.

**Operator-grepable substring:** `Collabhost admin key:` (on the `crit` line,
case-sensitive). Example:

```sh
# Linux / macOS
collabhost 2>&1 | tee first-boot.log
grep 'Collabhost admin key:' first-boot.log

# Windows (PowerShell)
.\collabhost.exe *>&1 | Tee-Object first-boot.log
Select-String 'Collabhost admin key:' first-boot.log
```

**Where the key shows up.** Wherever `collabhost`'s stdout goes. If you
launched the binary in a terminal, it prints there. If you piped stdout to a
log file or a log aggregator (`journald`, `rsyslog`, a cloud sink), the full
key lands in that sink too — you can recover it from the log export. **Rotate
the key in the dashboard once you have copied it** if you would rather the full
value not remain in your log backend indefinitely.

**Capture it immediately.** Copy the value from the `crit` line, save it in a
password manager or a secrets store. Paste it into the dashboard's login
prompt; it is also the `X-User-Key` header for API calls.

If you missed it on first boot, see §9 Troubleshooting → "Admin key missing
from scrollback."

**Configured admin keys.** If `COLLABHOST_ADMIN_KEY` (environment) or
`Auth:AdminKey` (appsettings.json) is set before first boot, Collabhost seeds
the admin user with that value silently — no stdout line, no generation. If
you set a configured key on a later boot and it doesn't match any existing
user, Collabhost inserts an additional `Admin (recovery)` user with that key as
a break-glass path; your original admin key still works. On every boot, a
configured key that matches an existing user is a no-op — this is the steady
state for operators who set `COLLABHOST_ADMIN_KEY` in a startup wrapper script.

---

## 3. Opening the dashboard

Once `collabhost` is running, the dashboard is at:

```
http://localhost:58400
```

A healthy first load:

- The login screen prompts for your admin key (from §2).
- After login, you land on an empty dashboard with the top-bar status strip
  showing `System: running`, `Proxy: running` (green dots).
- The app list is empty — you haven't registered any apps yet.

The proxy state on `/status` tells you whether the bundled Caddy came up:

```sh
curl http://localhost:58400/api/v1/status
```

Returns JSON including a `proxyState` field. Values:

| State | Meaning |
|-------|---------|
| `starting` | Caddy launch is in progress (transient, first few seconds of boot). |
| `running`  | Caddy is up and reachable. Normal healthy state. |
| `failed`   | Caddy did not come up within the startup probe deadline. See §9 Troubleshooting. |
| `disabled` | Caddy binary could not be resolved at startup. See §5 (`COLLABHOST_CADDY_PATH`). |
| `stopped`  | Caddy was started and has since stopped (e.g., crash post-boot). |

On the dashboard the same state renders as a color-coded cell with a detail
line beneath the state label:

| State | Dashboard color | Dashboard detail text |
|-------|-----------------|-----------------------|
| `starting` | amber  | `Warming up` |
| `running`  | green  | (none) |
| `failed`   | red    | `Check logs, restart Collabhost` |
| `disabled` | amber  | `Install Caddy or set COLLABHOST_CADDY_PATH` |
| `stopped`  | gray   | `Proxy app stopped` |

The JSON `proxyState` value is the contract; the color and detail text are
the operator-facing rendering of the same value.

Apps published through Collabhost get subdomains under the proxy's base domain
(`collab.internal` by default). You can change the base domain in
`appsettings.json` or via `COLLABHOST_PROXY_BASE_DOMAIN` (§5).

---

## 4. What's in this archive

```
collabhost[.exe]       The Collabhost API + control plane (this is the one you run)
caddy[.exe]            Pinned Caddy build used as the bundled reverse proxy
appsettings.json       Operator configuration (see §5)
INSTALL.md             This file
LICENSES/
  caddy-LICENSE        Caddy's Apache 2.0 license text
  caddy-NOTICE         Caddy's NOTICE file
wwwroot/               Dashboard static assets (HTML/JS/CSS for the operator console)
```

`data/` is created on first run next to the binary. It holds `collabhost.db`
(the SQLite database) and `data/backups/` (pre-migration backups — see §7).

---

## 5. Configuration

Collabhost has one configuration file — `appsettings.json` in the install
directory — with environment-variable overrides for operator-relevant
settings. Precedence (highest first):

```
environment variables > appsettings.json > built-in defaults
```

### 5.1 Environment variables (per-invocation overrides)

Environment variables are intended to be set in a **startup-script wrapper**
(`.sh` / `.ps1`) that launches Collabhost, not in `~/.bashrc` or system
properties. The operator picks one of two customization surfaces:

- **Edit `appsettings.json`** in place for persistent per-host configuration
  (preserved across reinstalls — see §8).
- **Set env vars in a wrapper script** for per-invocation overrides layered on
  top of `appsettings.json`.

The two are complementary.

### 5.2 Wrapper script example — Linux / macOS

Create `startup.sh` next to the `collabhost` binary:

```bash
#!/usr/bin/env bash
set -euo pipefail

export COLLABHOST_DATA_PATH=/srv/collabhost/data
export COLLABHOST_PROXY_BASE_DOMAIN=collabhost.lan

# exec replaces this shell so signals (SIGTERM/SIGINT) reach collabhost directly.
exec ./collabhost
```

Then `chmod +x startup.sh && ./startup.sh`.

### 5.3 Wrapper script example — Windows

Create `startup.ps1` next to `collabhost.exe`:

```powershell
$env:COLLABHOST_DATA_PATH        = 'D:\collabhost\data'
$env:COLLABHOST_PROXY_BASE_DOMAIN = 'collabhost.lan'

& .\collabhost.exe
```

Then `.\startup.ps1`.

### 5.4 Environment variables reference

| Variable | Overrides | Shape | Example |
|----------|-----------|-------|---------|
| `COLLABHOST_DATA_PATH`          | SQLite DB parent directory | Absolute directory path | `/srv/collabhost/data` |
| `COLLABHOST_USER_TYPES_PATH`    | `TypeStore:UserTypesDirectory` | Absolute directory path | `/srv/collabhost/user-types` |
| `COLLABHOST_CADDY_PATH`         | `Proxy:BinaryPath` — Caddy binary location | Absolute file path | `/usr/local/bin/caddy` |
| `COLLABHOST_HOSTING_LISTEN_PORT` | `Hosting:ListenPort` | Integer 1-65535 | `58400` |
| `COLLABHOST_PROXY_BASE_DOMAIN`  | `Proxy:BaseDomain` | Domain suffix | `collabhost.lan` |
| `COLLABHOST_PROXY_LISTEN_ADDRESS` | `Proxy:ListenAddress` | Caddy listen spec, comma-separated for multiple ports | `:80,:443` (default) or `:8080,:8443` |
| `COLLABHOST_PROXY_CERT_LIFETIME` | `Proxy:CertLifetime` | Caddy duration string | `720h` |
| `COLLABHOST_PORTAL_SUBDOMAIN`   | `Portal:Subdomain` — Portal route subdomain | DNS label | `portal` |
| `COLLABHOST_ADMIN_KEY`          | `Auth:AdminKey` | ULID / opaque string | `01JABCDEFGHJKMNPQRSTVWXYZ` |
| `COLLABHOST_INSTALL_BASE_URL`   | Install-script only — base URL for archive downloads. Overrides the default GitHub Releases URL. Useful for testing install scripts against local artifact servers. | URL (no trailing slash) | `http://localhost:9000/releases/v0.1.0` |

**Caddy binary resolution — two-tier precedence (highest first):**

1. `COLLABHOST_CADDY_PATH` — absolute path to any Caddy binary, set in your startup wrapper. Highest precedence; useful for per-invocation overrides.
2. `Proxy:BinaryPath` in `appsettings.json` — absolute path to a Caddy binary. The installer seeds this on first install with the bundled `caddy[.exe]` next to the Collabhost binary, so a fresh install works out of the box. Operator edits to this value are preserved across reinstalls — the installer only seeds when the key is absent or empty.

If neither resolves to an existing file, Collabhost boots with `proxyState = disabled` (see §3). To recover, re-run the installer (which will seed the key when absent), set `COLLABHOST_CADDY_PATH` in a startup wrapper, or write an absolute path into `Proxy:BinaryPath` directly.

Operator-relevant framework-standard variables (not Collabhost-specific):

| Variable | Purpose |
|----------|---------|
| `ASPNETCORE_ENVIRONMENT` | Standard .NET env var. Keep unset or `Production` for production installs. |
| `Logging__LogLevel__Default` | Standard ASP.NET Core log-level knob. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Standard OpenTelemetry OTLP endpoint (no-op when empty). |

Setting a blank / whitespace value for any `COLLABHOST_*` variable is treated
as unset — the effective value falls through to `appsettings.json`, then to
the built-in default. This blank-is-unset behavior is Collabhost-specific and
only applies to the `COLLABHOST_*` variables above.

### 5.5 Running as a service

Three service-wrapper shapes ship in v1: two on Linux (covered by Collabhost
itself) and an operator-driven path on Windows / macOS (no shipped template,
documented as a worked recipe). On Linux, pick based on who owns the host:

| Shape | Best for | Layout | Privileges |
|-------|----------|--------|------------|
| **User-scope** (`install.sh` + `collabhost.user.service`) | Single-operator homelab; you own the box and your shell. No admin handoff planned. | Everything in `~/.collabhost/`. Service runs as your login user. | None beyond your shell. `loginctl enable-linger` keeps the unit alive across logout. |
| **System-scope** (`install-system.sh`) | Servers; multi-operator handoff; a "production" install you'd hand to a new admin without explanation. | Canonical `/opt/`, `/etc/`, `/var/lib/` split. Service runs as a dedicated `collabhost` system user. | Root (sudo) at install time and for upgrades. The running service is unprivileged. |

Both ship the same binary and the same config keys. The choice is about
**filesystem layout and who owns the process**, not features.

#### 5.5.1 User-scope service (default `install.sh` flow)

For a persistent user-scope install, a systemd unit template ships at:

```
https://github.com/MrBildo/collabhost/blob/main/systemd/collabhost.user.service
```

Download or copy the file into `~/.config/systemd/user/collabhost.service`,
edit the `WorkingDirectory` and `ExecStart` lines if you installed somewhere
other than `$HOME/.collabhost/bin`, and enable:

```sh
loginctl enable-linger "$USER"          # keep the user manager alive after logout
systemctl --user daemon-reload
systemctl --user enable --now collabhost.service
```

Logs flow to `journald`:

```sh
journalctl --user -u collabhost -f
```

The template carries commented-out `Environment=` lines for the common
operator-relevant overrides (`COLLABHOST_DATA_PATH`, `COLLABHOST_PROXY_BASE_DOMAIN`,
`COLLABHOST_ADMIN_KEY`, ACME provider tokens). See §5.4 for the full reference.

#### 5.5.2 System-scope service (`install-system.sh`)

Lays the canonical Linux server layout and installs a system-level systemd
unit running under a dedicated `collabhost` system user:

```sh
curl -fsSL https://mrbildo.github.io/collabhost/install-system.sh | sudo bash
```

Or download first and read before running (recommended for a server-class
install):

```sh
curl -fsSL https://mrbildo.github.io/collabhost/install-system.sh -o install-system.sh
less install-system.sh
sudo bash install-system.sh
```

Pin to a specific release with `--version vX.Y.Z` or `COLLABHOST_VERSION=vX.Y.Z`.

**What the script creates:**

| Path | Owner | Purpose |
|------|-------|---------|
| `/opt/collabhost/bin/collabhost`, `/opt/collabhost/bin/caddy` | root | Binaries (read-only for the service) |
| `/opt/collabhost/wwwroot/` | root | Portal SPA assets |
| `/opt/collabhost/INSTALL.md`, `/opt/collabhost/LICENSES/` | root | Documentation |
| `/etc/collabhost/appsettings.json` | root:collabhost (0640) | Operator-facing config |
| `/etc/collabhost/appsettings.shipped.json` | root:collabhost (0640) | Smart-merge baseline (do not edit) |
| `/var/lib/collabhost/data/` | collabhost:collabhost (0750) | SQLite DB + pre-migration backups |
| `/var/lib/collabhost/user-types/` | collabhost:collabhost (0750) | Operator-authored AppType JSON |
| `/var/lib/collabhost/caddy/` | collabhost:collabhost (0750) | Caddy CA / account / cert storage |
| `/var/log/collabhost/` | collabhost:collabhost (0750) | Crash logs |
| `/etc/systemd/system/collabhost.service` | root | System-scope systemd unit |

The systemd unit sets `AmbientCapabilities=CAP_NET_BIND_SERVICE` so the
bundled Caddy can bind `:80`/`:443` without root and **without** per-binary
`setcap`. This is the load-bearing line that keeps the system-install free of
the "cap stripped on `cp` upgrade" failure mode.

**Verify the install:**

```sh
systemctl status collabhost
journalctl -u collabhost --since '5 min ago'
curl http://localhost:58400/api/v1/status
```

On a fresh install, capture the admin key (it emits once on first boot — see
§2):

```sh
journalctl -u collabhost --since '5 min ago' | grep 'Collabhost admin key:'
```

**Customize the unit:** do not edit `/etc/systemd/system/collabhost.service`
directly — `install-system.sh` overwrites it on every upgrade. Instead, drop
overrides under `/etc/systemd/system/collabhost.service.d/`:

```sh
sudo systemctl edit collabhost.service     # opens an override editor
sudo systemctl daemon-reload
sudo systemctl restart collabhost
```

The override file you create is preserved across upgrades because systemd
merges drop-ins on top of the base unit.

**Edit `/etc/collabhost/appsettings.json`** for persistent config changes
(e.g., `Proxy:BaseDomain`, `Proxy:DnsProvider`). Changes survive upgrades —
the smart-merge in §8.2 preserves operator edits.

**Upgrade:**

```sh
sudo bash install-system.sh                # latest
sudo bash install-system.sh --version v1.2.3  # pinned
```

The unit is `restart`ed automatically at the end of the upgrade. Data and
Caddy storage are preserved; binaries, `wwwroot/`, the unit itself, and
`LICENSES/` are refreshed.

**Uninstall:**

```sh
sudo systemctl disable --now collabhost
sudo rm /etc/systemd/system/collabhost.service
sudo systemctl daemon-reload
sudo rm -rf /opt/collabhost /etc/collabhost /var/lib/collabhost /var/log/collabhost
sudo userdel collabhost     # group goes with --user-group convention
```

**If you already have a `/opt/collabhost/` install from before this script
existed,** review the script first (`less install-system.sh`) — `install-system.sh`
will overwrite `/opt/collabhost/bin/collabhost`, `/opt/collabhost/bin/caddy`,
`/opt/collabhost/wwwroot/`, `/opt/collabhost/INSTALL.md`, `/opt/collabhost/LICENSES/`,
and `/etc/systemd/system/collabhost.service`. It will smart-merge any existing
`/etc/collabhost/appsettings.json` (preserving operator edits) and leave
`/var/lib/collabhost/` untouched. If your hand-rolled install used different
paths (e.g., a different config dir or data root), consolidate first or stay
on your hand-rolled layout — `install-system.sh` does not relocate files from
non-canonical paths.

#### 5.5.3 Windows and macOS service equivalents

Collabhost does not ship a bundled service installer for Windows or macOS in
v1 — the binary runs cleanly in the foreground on both platforms, and the
service-wrapper layer is an explicit gap. Operators who want boot-survival on
those platforms drive a host-native service manager directly. Two known-good
shapes:

**Windows — NSSM (Non-Sucking Service Manager).** NSSM wraps any console binary
as a Windows service with restart-on-crash, stdout/stderr redirection, and a
configurable startup user. It is the de facto standard for hosting non-service
binaries on Windows.

```powershell
# Install NSSM (chocolatey: choco install nssm; or download from nssm.cc).
nssm install Collabhost "C:\Users\you\.collabhost\bin\collabhost.exe"
nssm set Collabhost AppDirectory "C:\Users\you\.collabhost\bin"
nssm set Collabhost Start SERVICE_AUTO_START
# Optional: redirect output for inspection / log aggregation.
nssm set Collabhost AppStdout "C:\Users\you\.collabhost\bin\collabhost.log"
nssm set Collabhost AppStderr "C:\Users\you\.collabhost\bin\collabhost.log"
nssm set Collabhost AppRotateFiles 1
nssm start Collabhost
```

The first-boot admin key (§2) lands in the redirected log file — search for
`Collabhost admin key:` (case-sensitive). If the service was installed
post-first-boot, the key was already emitted by the foreground run that
seeded the database.

Privileged-port bind (`:80` / `:443`) on Windows requires either running the
service as `LocalSystem` (NSSM default) or granting the running account port
ownership via `netsh http add urlacl`. The bundled Caddy uses the standard
Windows socket APIs, so no extra capability shim applies.

**macOS — launchd.** Drop a property-list at
`~/Library/LaunchAgents/dev.collabhost.plist` (per-user; service starts on
login) or `/Library/LaunchDaemons/dev.collabhost.plist` (system-wide; starts
at boot, requires root):

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN"
  "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>Label</key>             <string>dev.collabhost</string>
  <key>ProgramArguments</key>  <array>
    <string>/Users/you/.collabhost/bin/collabhost</string>
  </array>
  <key>WorkingDirectory</key>  <string>/Users/you/.collabhost/bin</string>
  <key>RunAtLoad</key>         <true/>
  <key>KeepAlive</key>         <true/>
  <key>StandardOutPath</key>   <string>/Users/you/.collabhost/bin/collabhost.log</string>
  <key>StandardErrorPath</key> <string>/Users/you/.collabhost/bin/collabhost.log</string>
</dict>
</plist>
```

Then `launchctl load ~/Library/LaunchAgents/dev.collabhost.plist`. Privileged
ports on macOS are unrestricted for processes launched by `launchd` regardless
of user, so binding `:443` "just works" without a capability dance.

Both wrappers are operator-authored — no Collabhost-shipped template, no
post-install verification beyond what the wrapper itself reports. If a
bundled installer for either platform proves valuable, it would land in a
later release; for now, the foreground-launch path (§1) plus the wrapper of
your choice is the supported shape.

### 5.6 App-directory layout convention

Collabhost does not enforce a layout for the apps it manages. The path you
pass during registration is what Collabhost reads; any directory that the
Collabhost process can see and (where applicable) execute is fair game. That
flexibility is intentional — operators with existing conventions should not
have to relocate their artifacts to satisfy a platform opinion.

That said, three conventions are common enough that picking one up-front
saves rework when a second or third app shows up:

| Convention | Shape | Best fit |
|------------|-------|----------|
| `/srv/<slug>/` | One directory per app under `/srv`. FHS-canonical for "service data of this host." | Linux servers; system-scope install (§5.5.2). |
| `/opt/<vendor>/<app>/` | Vendored layout under `/opt`. Mirrors the Collabhost system-install's own `/opt/collabhost/`. | Multi-vendor hosts where each upstream owns a directory. |
| `~/apps/<slug>/` | One directory per app under your home. Mirrors `~/.collabhost/`. | Workstation / homelab; user-scope install (§5.5.1). |

Pick whichever matches the rest of the host's conventions. The Collabhost
process needs read access to the directory and (for `dotnet-app`,
`nodejs-app`, `executable`) execute on the binary; for `static-site` apps,
read on the document root is sufficient.

A few practical notes:

- **Use the slug as the leaf directory name** when feasible. The slug is the
  durable external handle for the app (it survives renames and shows up in
  every URL, log line, and route). Aligning the on-disk path with the slug
  keeps `ps`, `journalctl`, and the registration form all telling the same
  story.
- **Keep app data outside the binary tree** if the app writes to disk. A
  pattern like `/srv/<slug>/bin/` (read-only artifacts) plus
  `/srv/<slug>/data/` (writable) keeps upgrades cleanly scoped — replacing
  `bin/` does not touch state.
- **System-scope installs imply system-readable paths.** The `collabhost`
  service user (created by `install-system.sh`, see §5.5.2) needs read /
  execute on whatever path you register. `/srv/<slug>/` and
  `/opt/<vendor>/<app>/` work without further action; `~/apps/<slug>/` does
  not, because home directories on Linux default to mode 0700.

The recommendation, when there is no other constraint, is `/srv/<slug>/` for
system-scope installs and `~/apps/<slug>/` for user-scope. Both are cheap to
walk away from later because nothing in Collabhost is keyed on the path.

---

## 6. macOS: first-run quarantine

macOS attaches a quarantine attribute to binaries downloaded from the
internet. Without removing it, you will see "`collabhost` cannot be opened
because the developer cannot be verified" the first time you run it.

Because Collabhost's binaries are not notarized in v1, you need to clear the
attribute manually. From the directory where you extracted the archive:

```sh
xattr -d com.apple.quarantine collabhost
xattr -d com.apple.quarantine caddy
```

Why: Apple requires an Apple Developer Program enrollment (and a per-release
notarization step) before binaries launch cleanly. Collabhost is skipping that
for v1 to avoid the $99/year enrollment friction. If macOS usage grows enough
to warrant it, a later release will notarize.

**The `install.sh` script runs these `xattr` commands for you automatically**
and prints a confirmation line:

```
Cleared macOS quarantine attribute on collabhost and caddy.
```

This section is for operators who download and extract the archive manually.

---

## 7. Verifying the install

Three positive signals confirm a working install:

### 7.1 Version check

```sh
collabhost --version
```

Should print:

```
Collabhost 0.1.0
```

(The version number matches the release you installed.)

### 7.2 Status endpoint

```sh
curl http://localhost:58400/api/v1/status
```

Returns JSON including `"status": "ok"` and `"proxyState": "running"`.
See §3 for the full `proxyState` value table.

### 7.3 Dashboard loads

Open `http://localhost:58400` in your browser. The login screen accepts your
admin key (§2). After login, the dashboard renders with a green system /
proxy status strip.

---

## 8. Updating

### 8.0 Before you upgrade

**Stop the running Collabhost process before re-running the install script.**
On Linux, the kernel refuses `cp`-style truncation of a binary that is mapped
into a running process — the script aborts with `cp: cannot create regular
file '...collabhost': Text file busy` and the install does not complete.
macOS and Windows are more forgiving, but the discipline is the same on every
platform: stop the service, run the installer, start the service.

For the install shapes documented in §5.5:

```sh
# User-scope (systemd user-unit, §5.5.1)
systemctl --user stop collabhost

# System-scope (systemd system-unit, §5.5.2)
sudo systemctl stop collabhost
```

```powershell
# Windows / NSSM (§5.5.3)
nssm stop Collabhost
```

```sh
# macOS / launchd (§5.5.3, user-agent shape)
launchctl unload ~/Library/LaunchAgents/dev.collabhost.plist
```

For a foreground launch (no service wrapper), `Ctrl-C` on the running process
is sufficient — Collabhost's shutdown path drains its supervised children
before exiting.

**Linux user-scope only — re-apply `cap_net_bind_service` after upgrade.** If
your user-scope install relies on `setcap cap_net_bind_service=+ep` to let
the bundled Caddy bind `:80` / `:443` without root, that capability is an
extended attribute on the binary inode and the install script's `cp` does
not preserve it. After the upgrade completes:

```sh
sudo setcap 'cap_net_bind_service=+ep' ~/.collabhost/bin/caddy
sudo getcap ~/.collabhost/bin/caddy   # should print: ...caddy cap_net_bind_service=ep
systemctl --user restart collabhost
```

This step does not apply to the system-scope install (§5.5.2) — the systemd
unit's `AmbientCapabilities=CAP_NET_BIND_SERVICE` grants the privilege to
the running process tree on every start, with no per-binary attribute to
preserve.

**Verify the smart-merge picked up new shipped keys.** After the upgrade,
inspect `appsettings.json` and confirm that any new keys advertised in the
release notes are present. If they are not, the smart-merge silently bailed
out (see §8.2 for what runs and when); reconcile by hand against
`appsettings.shipped.json` in the install directory.

To upgrade Collabhost, re-run the install script. The installer is
**merge-safe by construction:**

**Preserved on re-run:**

- `data/` — your SQLite database and any data the binary writes.
- `data/backups/` — pre-migration backups taken before upgrades (see §8.1).
- `appsettings.json` — your persistent configuration file. Smart-merged with
  the new shipped file on each upgrade so operator edits survive while new
  shipped defaults are picked up automatically (see §8.2).
- Caddy storage directory (default `~/.local/share/caddy/` for user-scope,
  `/var/lib/collabhost/caddy/` for system-scope) — your CA root, account
  keys, and per-host certificates carry across the upgrade so operator-trusted
  trust-store imports (see §9.11) keep working.

**Overwritten on re-run:**

- `collabhost[.exe]` binary.
- `caddy[.exe]` binary.
- `INSTALL.md`.
- `LICENSES/` directory contents.
- `wwwroot/` directory (dashboard static assets — overwritten on every reinstall).
- `appsettings.shipped.json` — sidecar baseline managed by the installer to
  drive the smart-merge in §8.2. Do not edit by hand.

### 8.1 First-boot after upgrade

On the first launch of a new binary against an existing database:

1. The startup preflight verifies the data directory is writable.
2. If the new binary has pending EF Core migrations, Collabhost takes a
   **pre-migration backup** at:

   ```
   data/backups/collabhost.db.bak-{yyyyMMddTHHmmssZ}-pre-v{fromSemver}-to-v{toSemver}
   ```

   Example: `collabhost.db.bak-20260420T143022Z-pre-v0.1.0-to-v0.2.0`.
3. Migrations apply. On success, Collabhost records the new version as the
   last-booted version and proceeds with boot.
4. On failure, Collabhost halts with a structured stderr block pointing at
   the backup just taken (see §9 Recovery).

Only the **five most recent** pre-migration backups are kept; the sixth
rotates out the oldest. Operators who want longer retention can symlink
`data/backups/` to any location they prefer.

### 8.2 New configuration keys in newer versions

The installer performs a three-way smart-merge of `appsettings.json` on every
upgrade: operator edits are preserved, defaults the operator never touched are
refreshed to the new shipped value, and brand-new keys in the shipped file are
added.

The merge runs the new release's `collabhost --merge-appsettings` subcommand
against three files: the operator's on-disk `appsettings.json`, the shipped
`appsettings.json` from the new archive, and a sidecar baseline kept at
`appsettings.shipped.json` that records what was last shipped to this install.
The baseline is what lets the merger distinguish operator-edited keys
(preserve) from untouched defaults (refresh).

The first upgrade *into* a smart-merge-aware release (operators coming from
v0.1.x) runs in conservative mode because no baseline exists yet — every
existing key in `appsettings.json` is preserved, only brand-new keys are
added. The baseline is seeded during that upgrade so the next upgrade has the
full three-way information available.

The merge writes atomically (write to a temp file, then rename), so a failed
or interrupted merge cannot truncate `appsettings.json`. On any error the
installer leaves the on-disk file untouched and prints a warning pointing at
the shipped file in the install dir for manual reconciliation.

The `appsettings.shipped.json` sidecar is managed by the installer — do not
edit it. Treating it like a normal config file would cause the next merge to
think operator-edited keys were untouched defaults and refresh them.

### 8.3 Migrating between hosts

To move a working Collabhost install to a new machine, copy the data
directory across — Collabhost's persistent state is contained there, and the
binary on the new host re-applies any pending schema migrations on first
boot.

The migration shape:

1. **Stop Collabhost cleanly on the source host.** A clean stop is what
   guarantees the SQLite WAL is checkpointed back into `collabhost.db`.
   After a clean shutdown, only `collabhost.db` exists in `data/` — no
   `collabhost.db-wal` or `collabhost.db-shm` sidecars. If those sidecars
   are present, the database was not fully checkpointed; restart Collabhost
   and stop it cleanly to flush.

   ```sh
   systemctl --user stop collabhost                # user-scope (§5.5.1)
   sudo systemctl stop collabhost                  # system-scope (§5.5.2)
   ls ~/.collabhost/bin/data/collabhost.db*        # only collabhost.db should be present
   ```

2. **Hash the database for cross-host integrity verification.**

   ```sh
   sha256sum ~/.collabhost/bin/data/collabhost.db   # or appropriate path
   ```

   Save the hash; you will compare it on the new host before starting
   Collabhost.

3. **Copy the data directory wholesale.** Use any transport you trust to
   preserve byte-exact contents (`rsync -av`, `scp -p`, `tar | ssh`,
   physical disk):

   ```sh
   rsync -av ~/.collabhost/bin/data/ newhost:~/.collabhost/bin/data/
   ```

   Copy the entire directory, not just `collabhost.db` — the `backups/`
   subdirectory holds your pre-migration backups (see §8.1) and the Caddy
   storage directory (configurable via `Proxy:StoragePath`, default
   `data/caddy/` for user-scope or `/var/lib/collabhost/caddy/` for
   system-scope) holds the proxy's CA, account, and certificate state. Lose
   that and your operator-trusted CA changes; every device on the LAN has
   to re-import a freshly generated root.

4. **Verify the hash on the new host.**

   ```sh
   sha256sum ~/.collabhost/bin/data/collabhost.db   # must match step 2
   ```

5. **Install Collabhost on the new host at the matching scope.** A
   user-scope source migrates cleanly to a user-scope target via `install.sh`;
   a system-scope source migrates to a system-scope target via
   `install-system.sh`. Cross-scope migrations (user → system or vice versa)
   require relocating the data directory into the new layout's expected path
   (`/var/lib/collabhost/data/` for system-scope) and matching the file
   ownership (`collabhost:collabhost` for the system service user).

6. **Start Collabhost on the new host.** First boot against an existing
   database runs the standard upgrade path (§8.1) — pre-migration backup if
   the new binary has pending schema migrations, then migrate, then proceed.
   A version-matched migration (same Collabhost version on both hosts) skips
   the migration step and boots directly.

7. **Verify the install (§7).** Version, status endpoint, dashboard load.
   Your existing admin key continues to work — the user table moves with
   the database, and the admin row's key is what authenticates you.

**What does not move with `data/`:** `appsettings.json` (operator-facing
config, including `Proxy:BaseDomain` and any other host-specific overrides).
Copy `appsettings.json` separately if your customizations should follow the
data, or re-derive them on the new host from the release defaults plus your
operator notes. The smart-merge in §8.2 will reconcile shipped defaults on
first upgrade after the move; persistent operator edits are yours to carry.

If your `Proxy:BaseDomain` references the source host (as in
`*.collab.internal` resolved by a router-side dnsmasq override pointing at
the source IP), the new host needs the LAN-side DNS updated in lockstep —
see §8.4 for the BaseDomain-change discipline that also applies here.

### 8.4 Changing your base domain

`Proxy:BaseDomain` controls the suffix Caddy uses to build app routes —
`<slug>.<base-domain>`. Changing it is a one-line edit, but the downstream
effects fan out further than the config-key shape suggests. Plan for the
change as a small migration, not a setting flip.

**What changes when `Proxy:BaseDomain` changes:**

- **Caddy regenerates every route** on the next proxy sync. The old
  `<slug>.<old-domain>` hostnames stop existing in the proxy config; new
  `<slug>.<new-domain>` hostnames take their place. Any external integration
  pointing at the old hostname starts failing immediately.
- **MCP servers and external integrations** registered against
  `https://<slug>.<old-domain>/mcp` (or any other path) need their
  configuration updated in lockstep. For Claude Code specifically, that
  means the `~/.claude.json` user scope or the project-scope `.mcp.json`,
  followed by a Claude Code restart to pick up the new URLs.
- **Browser bookmarks, dashboard URL, deep links into individual app routes**
  all change. The Collabhost dashboard's own subdomain
  (`collabhost.<base-domain>` by default — see `Portal:Subdomain` in §5.4)
  follows the same rule.
- **ACME issuer interaction.** Switching from `*.collab.internal` (Caddy
  internal CA) to a real public domain (e.g. `*.example.com`) flips the
  issuer that Caddy uses. With `Proxy:DnsProvider` set, Caddy issues real
  Let's Encrypt certs via DNS-01; without it, Caddy attempts HTTP-01 against
  the new hostnames, which only works if the new domain resolves publicly
  to your host on `:80`. Going the other direction (real domain →
  `*.collab.internal`) flips back to the internal CA, and every device on
  the LAN that previously trusted Let's Encrypt now needs the bundled
  internal-CA root imported (see §9.11).
- **LAN-side DNS.** `*.collab.internal` is not a public TLD; resolution
  requires either per-host `hosts` entries or a router / local DNS server
  mapping `*.collab.internal` to the Collabhost host's IP. Changing the
  base domain to a different non-public suffix moves the dnsmasq / DNS
  override along with it — `address=/old-domain/<host-ip>` becomes
  `address=/new-domain/<host-ip>`.

**The recommended sequence:**

1. **Pick the new domain and decide the issuer.** Public domain with DNS
   API access → Let's Encrypt via `Proxy:DnsProvider`. Internal-only
   domain → Caddy internal CA (the default).
2. **Update DNS first.** Public domain: confirm the wildcard A record
   resolves to your host's IP from a public resolver. Internal domain:
   update the router-side dnsmasq override (or per-host `hosts` entries)
   to map `*.<new-domain>` to the host IP.
3. **Update `appsettings.json`.** Set `Proxy:BaseDomain` to the new value;
   if switching to LE, set `Proxy:DnsProvider` and arrange for the
   provider's API token in the process environment (see the systemd unit
   templates under `systemd/` for the env-var injection pattern).
4. **Restart Collabhost.** Caddy regenerates routes on next sync;
   certificates are issued (or re-loaded from cache) on the first request
   to each new hostname.
5. **Update every external integration** that references the old hostnames.
   For Claude Code MCP configs:

   ```sh
   # Find references to update.
   grep -r '<old-domain>' ~/.claude.json ~/projects/*/.mcp.json 2>/dev/null
   ```

   Replace and restart Claude Code so the new URLs are picked up.

6. **Trust the new CA on every device** if you switched to (or stayed on)
   the internal CA. See §9.11 for the per-OS walkthrough.

The old certificates remain in Caddy's storage directory after the change
(under `Proxy:StoragePath`); they are inert as long as no route asks for
them. Pruning is a manual sweep if you want the bytes back.

---

## 9. Troubleshooting

### 9.1 "Caddy did not start" (`proxyState = "failed"`)

Check `/api/v1/status` — if `proxyState` is `"failed"`, Caddy launched but did
not pass the startup probe within the deadline.

Options:

- Set `COLLABHOST_CADDY_PATH` (env var) or `Proxy:BinaryPath` (appsettings.json) to the
  absolute path of a known-working Caddy binary on the host
  (e.g., `/usr/local/bin/caddy`). The env var takes precedence; see §5.4 for the
  full two-tier resolution chain.
- Verify the bundled `caddy` binary is executable (`chmod +x caddy`).
- Check structured logs for the Caddy process's stderr.

### 9.2 Port 443 (or 80) already in use

Another proxy or service on the host owns `:80` or `:443` (or both — the default
`Proxy:ListenAddress` is `:80,:443`). Edit `Proxy:ListenAddress` in
`appsettings.json` to free ports (e.g., `:8080,:8443`) or set
`COLLABHOST_PROXY_LISTEN_ADDRESS`. The value is a comma-separated list of Caddy
listen specs — remember to include the colon and to omit any spaces if shell
quoting strips them.

If `proxyState` on `/api/v1/status` reports `degraded`, the proxy process is
alive but routes are not reaching the public listener. The `proxyDetail` block
on the same response carries the specific error from Caddy (e.g., `bind:
permission denied` when a privileged port can't be claimed). On Linux,
`cap_net_bind_service` on the Caddy binary is the standard fix for `:80` /
`:443`; alternatively switch to `:8080,:8443` and front Collabhost behind another
proxy or a router NAT rule. See §9.10.1 for the full layer-1 diagnostic.

### 9.3 Port 58400 already in use

Another service on the host owns the Collabhost API port. Edit
`Hosting:ListenPort` in `appsettings.json` (or set
`COLLABHOST_HOSTING_LISTEN_PORT`) to a free port, and update any bookmarks /
scripts that hit the dashboard at `http://localhost:58400`.

### 9.4 Collabhost won't launch on macOS

See §6. The quarantine attribute needs clearing. If you used `install.sh`
and still see the message, re-run `xattr -d com.apple.quarantine collabhost`
manually.

### 9.5 SQLite file permission errors on Linux

Check ownership of `$HOME/.collabhost/bin/data/`. The Collabhost process
must own the directory and the SQLite database inside. Collabhost creates
`data/` 0700 on first run; if you moved the binary or changed users,
re-create the directory with the correct owner.

### 9.6 Admin key missing from scrollback

The admin key emits once, to stdout, on first launch against an empty database.
If your terminal scrollback has lost it:

- **Check your log destination first.** The full key emits as a `crit`-level
  ILogger line, so it lands wherever `collabhost`'s stdout goes — including
  `journald`, `rsyslog`, a file redirect, or a cloud log sink. Search for the
  substring `Collabhost admin key:` in your log backend before attempting
  recovery:

  ```sh
  journalctl -u collabhost | grep 'Collabhost admin key:'
  grep 'Collabhost admin key:' /path/to/collabhost.log
  ```

- **Break-glass on a running install (Scenario 3).** Set `COLLABHOST_ADMIN_KEY`
  to a new ULID and relaunch. If the configured key doesn't match any existing
  user, Collabhost inserts an additional `Admin (recovery)` user with that key.
  Use it to log in and then edit or disable other users from the dashboard.

- **Delete the user from the DB and restart (Scenario 1).** Stop Collabhost,
  delete the admin user row from `collabhost.db`, and relaunch. Collabhost
  will treat the empty database as a first-run and regenerate a fresh admin key.
  Use this only if you have no other data to preserve.

- **Set a configured key up front (Scenario 2).** On a fresh install, set
  `COLLABHOST_ADMIN_KEY` (or `Auth:AdminKey` in `appsettings.json`) **before**
  first boot. Collabhost seeds the admin user with that value silently.

- **Redirect stdout on the next fresh install.** Before the first launch
  against a fresh `data/`, run:

  ```sh
  ./collabhost 2>&1 | tee collabhost-firstboot.log
  grep 'Collabhost admin key:' collabhost-firstboot.log
  ```

  The `crit` line containing the full ULID is what you want.

UX improvements here are tracked in a follow-up; see the GitHub release notes
for your version.

### 9.7 Binary crashes before I see anything

Collabhost writes a crash log to disk on startup failure or unhandled exception.
Look in:

```
~/.collabhost/data/logs/         (Linux / macOS)
%USERPROFILE%\.collabhost\data\logs\   (Windows)
```

Each crash produces a `collabhost-crash-<utc-timestamp>.log` file containing the
same summary, details, and recovery steps printed to stderr, plus the exception
stack trace where applicable. The directory keeps the last 10 crash logs and
prunes older ones automatically.

The crash log directory is configurable:

- Environment variable: `COLLABHOST_LOGS_PATH=/some/other/dir`.
- Appsetting: `"Diagnostics": { "CrashLogs": { "Directory": "..." } }`.
- Retention count: `"Diagnostics": { "CrashLogs": { "Retention": 25 } }`.

If you need stdout + stderr captured as well (e.g., for an issue report),
redirect the process output:

```sh
./collabhost > collabhost.log 2>&1
```

```powershell
.\collabhost.exe *> collabhost.log
```

### 9.8 Recovery after a failed upgrade (migration failure)

If a migration fails during upgrade, Collabhost halts with a stderr block
like:

```
Collabhost startup failed: migration failed

Details:
  - Backup created at: /home/user/.collabhost/bin/data/backups/collabhost.db.bak-20260420T143022Z-pre-v0.1.0-to-v0.2.0
  - Migration attempted: AddActivityEvents
  - Exception: System.Data.SqliteException: SQLITE_ERROR: no such column: Foo

Recovery:
  1. See INSTALL.md -> Troubleshooting -> "If an upgrade fails."
  2. Restore the backup listed above.
  3. Re-install the previous version.

Exit code: 20
```

Steps:

1. `cd $HOME/.collabhost/bin/data/`.
2. `ls backups/` to see your pre-migration backups.
3. Copy the most recent backup over the live DB:

   ```sh
   cp backups/collabhost.db.bak-20260420T143022Z-pre-v0.1.0-to-v0.2.0 collabhost.db
   ```

   ```powershell
   Copy-Item backups\collabhost.db.bak-20260420T143022Z-pre-v0.1.0-to-v0.2.0 collabhost.db -Force
   ```
4. Re-install the previous version (rerun `install.sh` / `install.ps1` with
   `--version v0.1.0` / `-Version v0.1.0`).
5. Boot against the restored DB to confirm service is healthy.
6. File an issue with the stderr block and the migration name.

If the exit block says `pre-migration backup failed`, skip the restore step —
your existing database is intact (the migration never ran). Verify disk space
and data-directory permissions, then retry the upgrade.

Collabhost does not attempt to auto-rollback because SQLite migrations are
not always transactional across multiple statements — the pre-migration
backup is the rollback mechanism.

### 9.9 Exit codes

For operators running Collabhost under a supervisor (systemd, NSSM, etc.),
the exit-code taxonomy on startup failure:

| Code | Meaning | Actionable by |
|------|---------|---------------|
| 0  | Normal shutdown | — |
| 1  | Generic unhandled exception | Report an issue |
| 10 | Data directory not writable | Operator (filesystem perms) |
| 11 | Database locked or backup filename collision | Operator (stop any other Collabhost instance; wait and retry) |
| 20 | Migration failed or pre-migration backup failed | Operator (restore backup — see §9.8) |
| 30 | Built-in type validation failure | Collabhost team (packaging bug) |
| 31 | User-type validation failure | Operator (fix / remove the bad JSON) |
| 40 | Seeder threw unexpectedly (proxy or admin) | Operator (file an issue) |

Any code outside this table is unexpected — please file an issue with the
stderr block.

**Stability note:** specific codes are informational in v0.1.0 and may be
refined across minor versions until the first operator writes automation
that depends on them. If you are building such automation, open an issue so
the contract can be frozen.

### 9.10 First remote dashboard visit — diagnostic walkthrough

The first time you open the Collabhost dashboard from a different device on
your LAN (laptop, phone, tablet), four layers of friction can stack on top
of each other and produce a single opaque "spins forever" symptom. The
combination is hard to debug from the receiving end because the browser does
not distinguish "no DNS" from "DNS resolved but no route" from "route exists
but cert untrusted." Walk the layers in order.

The four layers, from closest-to-the-host to closest-to-the-browser:

| # | Layer | Symptom | Resolution path |
|---|-------|---------|-----------------|
| 1 | Privileged-port bind | `proxyState: "degraded"` on `/api/v1/status` (proxy alive, public listener not bound) | §5.5.2 system-scope (resolves by construction) or `setcap` workaround (user-scope) |
| 2 | DNS | Browser shows "server not found" / `ERR_NAME_NOT_RESOLVED` | LAN DNS override or per-device `hosts` entry |
| 3 | Host firewall | Browser hangs (`ERR_CONNECTION_TIMED_OUT`) | `ufw` / `firewalld` rule for `:443` |
| 4 | CA trust | Browser shows "Your connection is not private" | Per-device CA import — see §9.11 |

#### 9.10.1 Layer 1 — Privileged-port bind

The bundled Caddy binds `:443` (and `:80` for HTTP→HTTPS redirect). On
Linux, binding ports below 1024 requires `CAP_NET_BIND_SERVICE` or root.

**Diagnose from the Collabhost host:**

```sh
curl http://localhost:58400/api/v1/status | grep -E '"proxyState"|"proxyDetail"'
```

A healthy response shows `"proxyState": "running"` with no `proxyDetail`
block. A degraded response shows `"proxyState": "degraded"` with a
`proxyDetail` block carrying Caddy's specific bind error
(`bind: permission denied` is the privileged-port symptom). Cross-reference
the dashboard System page for the same information rendered.

**Resolve:**

- **System-scope install (§5.5.2):** by construction, the systemd unit's
  `AmbientCapabilities=CAP_NET_BIND_SERVICE` grants the privilege. If you
  see `permission denied` here, the unit was modified or replaced — check
  `systemctl cat collabhost.service` for the directive.
- **User-scope install (§5.5.1) or workstation foreground launch:** apply
  `setcap` to the bundled Caddy binary:

  ```sh
  sudo setcap 'cap_net_bind_service=+ep' ~/.collabhost/bin/caddy
  systemctl --user restart collabhost   # or restart the foreground process
  ```

  Re-apply this after every upgrade (see §8.0). To avoid the recurring
  step, switch to the system-scope install or change `Proxy:ListenAddress`
  to non-privileged ports (`:8080,:8443`) and front Collabhost behind an
  upstream proxy or router NAT rule that owns `:443`.

#### 9.10.2 Layer 2 — DNS

By default, Collabhost routes traffic through `<slug>.collab.internal`.
`.internal` is a non-public TLD, so a fresh browser cannot resolve it
without local help.

**Diagnose from the device opening the dashboard:**

```sh
# Linux / macOS
dig collabhost.collab.internal +short
nslookup collabhost.collab.internal

# Windows (PowerShell)
Resolve-DnsName collabhost.collab.internal
```

If the resolver returns nothing, DNS is the layer. If it returns the
Collabhost host's IP, move to layer 3.

**Resolve — pick one:**

- **Per-device `hosts` entry** (cheapest, smallest blast radius). One line
  per app subdomain in `/etc/hosts` (Linux / macOS) or
  `C:\Windows\System32\drivers\etc\hosts` (Windows):

  ```
  192.168.50.135   collabhost.collab.internal
  192.168.50.135   <slug>.collab.internal
  ```

  Repeat the `hosts` edit on every device that needs to reach the
  dashboard.

- **LAN-side DNS wildcard** (covers every device on the LAN at once). On a
  router running dnsmasq (e.g., Asuswrt-Merlin, OpenWrt), append to
  `dnsmasq.conf`:

  ```
  address=/collab.internal/192.168.50.135
  ```

  Restart dnsmasq (`service restart_dnsmasq` on Asuswrt-Merlin). On a
  pi-hole / dedicated DNS server, the equivalent local-records mechanism
  applies. After this, every device that uses the router for DNS resolves
  `*.collab.internal` automatically.

- **Switch to a real domain.** If the dashboard will be reached from
  devices outside your LAN, set `Proxy:BaseDomain` to a domain you own,
  point a wildcard A record at the Collabhost host, and (for browsers
  that won't trust an internal CA) configure the DNS-01 ACME issuer for
  Let's Encrypt certs (see §8.4 for the full BaseDomain-change shape).

#### 9.10.3 Layer 3 — Host firewall

A clean Ubuntu / Fedora server with `ufw` or `firewalld` enabled denies
inbound `:443` by default. `nmap` from the device toward the host
distinguishes this from a DNS or routing issue.

**Diagnose from the device opening the dashboard:**

```sh
# Replace 192.168.50.135 with the Collabhost host's IP.
nmap -p 443 192.168.50.135                    # nmap on the device

# Or test connect directly (no curl request, just TCP):
nc -zv 192.168.50.135 443                     # POSIX
Test-NetConnection 192.168.50.135 -Port 443   # Windows PowerShell
```

`filtered` from `nmap`, or a hang from `nc` / `Test-NetConnection`,
points at the host firewall. `closed` would point at Caddy not bound
(layer 1).

**Resolve on the Collabhost host:**

```sh
# ufw (Ubuntu / Debian default)
sudo ufw allow from 192.168.50.0/24 to any port 443 proto tcp
sudo ufw status                                # confirm rule landed

# firewalld (Fedora / RHEL default)
sudo firewall-cmd --add-service=https --permanent
sudo firewall-cmd --reload
```

Scope the rule to your LAN's CIDR rather than `any` if Collabhost is
intentionally LAN-only — exposing `:443` to the public internet without a
public-cert posture (LE) and an explicit Caddy listen-address change is a
separate decision, not the default.

#### 9.10.4 Layer 4 — CA trust

If layers 1-3 pass and the browser reaches Caddy, the last hurdle is the
TLS handshake. With the default `*.collab.internal` setup, Caddy presents
a certificate signed by its internal CA — which no browser trusts out of
the box. Browsers show "Your connection is not private" / `NET::ERR_CERT_AUTHORITY_INVALID`.

This is its own walkthrough — see §9.11.

#### 9.10.5 End-to-end success signature

When all four layers pass, opening `https://collabhost.collab.internal`
from a LAN device shows the Collabhost login screen, the URL bar shows
the lock icon (no warning), and `curl -v https://collabhost.collab.internal/api/v1/status`
from a terminal on the same device returns JSON with `"status": "ok"`
and no certificate error in the verbose output.

### 9.11 Trusting the bundled Caddy internal CA

When `Proxy:BaseDomain` is `collab.internal` (the default), the bundled
Caddy issues per-host certificates from its own internal CA. Operators
running their own real domain with Let's Encrypt via DNS-01 ACME (see
§8.4) can skip this section entirely — LE's root is in every browser and
OS trust store by default.

For operators on the default internal-CA path, the discipline is: import
Caddy's internal-CA root certificate into each device's trust store. Do
this once per device that will reach the dashboard.

#### 9.11.1 Where the root cert lives

Caddy stores its CA material under whatever `Proxy:StoragePath` resolves to.
The root certificate sits at:

```
<Proxy:StoragePath>/pki/authorities/local/root.crt
```

For the default install paths:

| Install shape | `Proxy:StoragePath` | Root cert path |
|---------------|---------------------|----------------|
| User-scope (Linux, `install.sh`) | unset → Caddy default | `~/.local/share/caddy/pki/authorities/local/root.crt` |
| User-scope (macOS, `install.sh`) | unset → Caddy default | `~/Library/Application Support/caddy/pki/authorities/local/root.crt` |
| System-scope (Linux, `install-system.sh`) | `/var/lib/collabhost/caddy` (set in the systemd unit) | `/var/lib/collabhost/caddy/pki/authorities/local/root.crt` |
| Workstation Windows | unset → Caddy default | `%AppData%\Caddy\pki\authorities\local\root.crt` |

If unsure, search:

```sh
sudo find / -name root.crt -path '*pki/authorities/local/*' 2>/dev/null
```

Copy the file off the Collabhost host to each device that needs to
trust it. The cert is public information (it has to be, to be useful) —
treat the corresponding `root.key` as a secret and never copy it.

#### 9.11.2 Per-OS import

**Windows (administrator PowerShell):**

```powershell
# Imports into the Local Machine trust store; trusts for every user on the device.
Import-Certificate -FilePath .\root.crt -CertStoreLocation Cert:\LocalMachine\Root
```

For per-user trust without admin, swap `Cert:\LocalMachine\Root` for
`Cert:\CurrentUser\Root`. Brave, Edge, and other Chromium-family browsers
read from the OS trust store; Firefox uses its own store and requires a
separate import via `about:preferences#privacy → Certificates → View
Certificates → Authorities → Import`.

**Linux (Debian / Ubuntu):**

```sh
sudo cp root.crt /usr/local/share/ca-certificates/collabhost-caddy.crt
sudo update-ca-certificates
# Verify with: openssl s_client -connect collabhost.collab.internal:443 -showcerts
```

**Linux (Fedora / RHEL):**

```sh
sudo cp root.crt /etc/pki/ca-trust/source/anchors/collabhost-caddy.crt
sudo update-ca-trust
```

Or via the per-user p11-kit shim (no root):

```sh
trust anchor --store root.crt
```

Firefox on Linux maintains its own trust store, same as Windows — import
under `about:preferences#privacy`.

**macOS:**

```sh
# System-wide trust (requires admin).
sudo security add-trusted-cert -d -r trustRoot \
  -k /Library/Keychains/System.keychain root.crt

# Or per-user (no admin).
security add-trusted-cert -d -r trustRoot \
  -k ~/Library/Keychains/login.keychain-db root.crt
```

Safari and Chromium browsers read the system / login keychain. Firefox on
macOS uses its own store.

**iOS:**

Email or AirDrop the `root.crt` to the device, tap to install the
profile, then enable full trust in **Settings → General → About →
Certificate Trust Settings → enable for "Caddy Local Authority"**. The
two-step install / explicit-trust dance is iOS's standard root-cert
posture; without the second step the cert is installed but not trusted.

**Android:**

Settings location varies by manufacturer; the canonical path is
**Settings → Security → Encryption & credentials → Install a
certificate → CA certificate**, then select the file. Recent Android
versions show a strong warning before installing a CA — accepting it is
the right move for a personal-LAN root cert; not for arbitrary downloads
from the internet.

#### 9.11.3 Per-device discipline

Every device that opens the dashboard or hits an app's HTTPS route
needs the root cert imported once. New laptops, new phones, replacement
tablets, fresh OS installs all repeat the step. There is no
per-organization push mechanism in the default Collabhost shape — that
is the trade-off that comes with running your own CA on a non-public
domain. Operators who outgrow the per-device ceremony move to a public
domain plus Let's Encrypt (§8.4); LE's root is already in every device's
trust store, so the import step disappears.

The Caddy root cert rotates only when its underlying private key
rotates (uncommon — operator-driven via Caddy storage manipulation, not
a routine event). Day-to-day per-host certificates are short-lived and
auto-renewed by Caddy without operator action; once the root is trusted,
the per-host renewals carry the trust automatically.

---

## 10. Uninstall

There is no uninstall script. To remove Collabhost:

```sh
rm -rf $HOME/.collabhost
```

```powershell
Remove-Item -Recurse -Force $HOME\.collabhost
```

If you used `COLLABHOST_DATA_PATH` to move the data directory outside
`$HOME/.collabhost`, delete that location as well.

Remove the PATH entry from your shell RC file (`~/.bashrc`, `~/.zshrc`,
`~/.profile`) or User PATH (Windows) manually if you want to clean that up
too.

On Linux / macOS, a `sed` one-liner removes the installer's two-line PATH
block (`# Added by collabhost installer` + the `export PATH=...` line below
it):

```sh
sed -i '/# Added by collabhost installer/,+1d' ~/.bashrc    # or ~/.zshrc / ~/.profile
```

(GNU `sed`. On macOS use `sed -i ''` with the same expression.)

On Windows, open **Settings → System → About → Advanced system settings →
Environment Variables** (or run `SystemPropertiesAdvanced`) and remove the
`%USERPROFILE%\.collabhost\bin` entry from your **User PATH**. The installer
does not write to System PATH.

---

## 11. Verifying checksums (manual downloads)

If you downloaded an archive directly from the Releases page, you can verify
the checksum against the release's `checksums.txt`.

```sh
# Linux
sha256sum -c collabhost-0.1.0-linux-x64.tar.gz.sha256
```

```sh
# macOS
shasum -a 256 -c collabhost-0.1.0-osx-arm64.tar.gz.sha256
```

```powershell
# Windows
$expected = (Get-Content checksums.txt | Select-String 'collabhost-0.1.0-win-x64.zip$').Line -split '\s+' | Select-Object -First 1
if (-not $expected) { throw 'Checksum line not found for this archive.' }
$actual   = (Get-FileHash -Algorithm SHA256 collabhost-0.1.0-win-x64.zip).Hash.ToLower()
if ($expected -ne $actual) { throw 'Checksum mismatch.' } else { 'OK' }
```

The install scripts do this automatically — failure to match aborts the
install before extraction.
