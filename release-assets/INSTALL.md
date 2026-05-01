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
4. Starts the bundled Caddy reverse proxy on `:443`.
5. Listens on `http://localhost:58400` for the dashboard.

Open `http://localhost:58400` in your browser. See §2 to capture the admin key
and §3 for what a healthy first load looks like.

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
| `COLLABHOST_PROXY_LISTEN_ADDRESS` | `Proxy:ListenAddress` | Caddy listen spec | `:8443` |
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

To upgrade Collabhost, re-run the install script. The installer is
**merge-safe by construction:**

**Preserved on re-run:**

- `data/` — your SQLite database and any data the binary writes.
- `data/backups/` — pre-migration backups taken before upgrades (see §8.1).
- `appsettings.json` — your persistent configuration file. Smart-merged with
  the new shipped file on each upgrade so operator edits survive while new
  shipped defaults are picked up automatically (see §8.2).

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

### 9.2 Port 443 already in use

Another proxy or service on the host owns `:443`. Edit
`Proxy:ListenAddress` in `appsettings.json` to a free port (e.g., `:8443`)
or set `COLLABHOST_PROXY_LISTEN_ADDRESS`. Remember to include the colon.

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
