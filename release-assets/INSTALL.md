# Collabhost — Install

A self-hosted application platform for services running on your own machine.
This guide walks you from extracted archive to logged-in dashboard.

**Supported platforms:** Linux (x64, arm64), macOS (x64, arm64), Windows (x64).
win-arm64 is not supported in v1.

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

On the **very first launch** against an empty database, Collabhost generates an
admin authentication key (a ULID) and writes it to stdout exactly once:

```
[Collabhost] Admin key: 01JABCDEFGHJKMNPQRSTVWXYZ
```

This is the only time the full key is printed. The structured log emits a
truncated hint (`01JABCDE...`) so an operator scanning logs can confirm the
seed happened, but cannot recover the key from log exports.

**Capture it immediately.** Copy the value, save it in a password manager or a
secrets store. Paste it into the dashboard's login prompt; it is also the
`X-User-Key` header for API calls.

If you missed it on first boot, see §9 Troubleshooting → "Admin key missing
from scrollback."

**Configured admin keys.** If `COLLABHOST_ADMIN_KEY` (environment) or
`Auth:AdminKey` (appsettings.json) is set before first boot, Collabhost seeds
the admin user with that value silently — no stdout line, no generation. If
you set a configured key on a later boot and it doesn't match any existing
user, Collabhost inserts an additional `Admin (recovery)` user with that key as
a break-glass path; your original admin key still works.

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
| `COLLABHOST_CADDY_PATH`         | Caddy binary location | Absolute file path | `/usr/local/bin/caddy` |
| `COLLABHOST_PROXY_BASE_DOMAIN`  | `Proxy:BaseDomain` | Domain suffix | `collabhost.lan` |
| `COLLABHOST_PROXY_LISTEN_ADDRESS` | `Proxy:ListenAddress` | Caddy listen spec | `:8443` |
| `COLLABHOST_PROXY_CERT_LIFETIME` | `Proxy:CertLifetime` | Caddy duration string | `720h` |
| `COLLABHOST_PROXY_SELF_PORT`    | `Proxy:SelfPort` | Integer 1-65535 | `58400` |
| `COLLABHOST_ADMIN_KEY`          | `Auth:AdminKey` | ULID / opaque string | `01JABCDEFGHJKMNPQRSTVWXYZ` |

Operator-relevant framework-standard variables (not Collabhost-specific):

| Variable | Purpose |
|----------|---------|
| `ASPNETCORE_ENVIRONMENT` | Standard .NET env var. Keep unset or `Production` for production installs. |
| `Logging__LogLevel__Default` | Standard ASP.NET Core log-level knob. |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Standard OpenTelemetry OTLP endpoint (no-op when empty). |

Setting a blank / whitespace value for any `COLLABHOST_*` variable is treated
as unset — the effective value falls through to `appsettings.json`, then to
the built-in default.

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

Returns JSON including `"status": "running"` and `"proxyState": "running"`.
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

- `data/` — your SQLite database, pre-migration backups, and any data the
  binary writes.
- `appsettings.json` — your persistent configuration file.

**Overwritten on re-run:**

- `collabhost[.exe]` binary.
- `caddy[.exe]` binary.
- `INSTALL.md`.
- `LICENSES/` directory contents.

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

Because `appsettings.json` is preserved, an operator on v0.1.0's shipped file
does not automatically pick up a newly shipped configuration key in v0.2.0+.

The discipline that makes this safe: **every new shipped key ships with a
working in-code default**, so boot does not break when the key is absent from
an older `appsettings.json`. Release notes call out any new keys worth
enabling.

If you want to incorporate shipped defaults from a new release into your
existing `appsettings.json`, diff it against `appsettings.json` extracted
from the archive of the new version and merge by hand. A smart-merge is
planned but not in v0.1.0.

---

## 9. Troubleshooting

### 9.1 "Caddy did not start" (`proxyState = "failed"`)

Check `/api/v1/status` — if `proxyState` is `"failed"`, Caddy launched but did
not pass the startup probe within the deadline.

Options:

- Set `COLLABHOST_CADDY_PATH` to a known-working Caddy binary on the host
  (e.g., a system-installed Caddy at `/usr/local/bin/caddy`).
- Verify the bundled `caddy` binary is executable (`chmod +x caddy`).
- Check structured logs for the Caddy process's stderr.

### 9.2 Port 443 already in use

Another proxy or service on the host owns `:443`. Edit
`Proxy:ListenAddress` in `appsettings.json` to a free port (e.g., `:8443`)
or set `COLLABHOST_PROXY_LISTEN_ADDRESS`. Remember to include the colon.

### 9.3 Port 58400 already in use

Another service on the host owns the Collabhost API port. Edit
`Proxy:SelfPort` in `appsettings.json` (or set `COLLABHOST_PROXY_SELF_PORT`)
to a free port, and update any bookmarks / scripts that hit the dashboard at
`http://localhost:58400`.

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

The admin key is only printed once, to stdout, on first launch against an
empty database. If your terminal scrollback has lost it, recovery options
are limited in v0.1.0:

- **Redirect stdout on first boot.** Before the first launch against a fresh
  `data/`, run:

  ```sh
  ./collabhost > collabhost-firstboot.log 2>&1
  ```

  Inspect the log for the `[Collabhost] Admin key: ...` line, then move it
  somewhere safe.
- **Set a configured key up front.** On a fresh install, set
  `COLLABHOST_ADMIN_KEY` (or `Auth:AdminKey` in `appsettings.json`) **before**
  first boot. Collabhost will seed the admin user with that value silently.
- **Break-glass on a running install.** Set `COLLABHOST_ADMIN_KEY` to a new
  ULID and relaunch. If the configured key doesn't match any existing user,
  Collabhost inserts an additional `Admin (recovery)` user with that key.
  Use it to log in and then edit / disable other users from the dashboard.

UX improvements here are tracked in a follow-up; see the GitHub release notes
for your version.

### 9.7 Binary crashes before I see anything

Run Collabhost with stdout + stderr redirected so you can read the failure
after the fact:

```sh
./collabhost > collabhost.log 2>&1
```

```powershell
.\collabhost.exe *> collabhost.log
```

Inspect `collabhost.log`. Structured log-directory support is planned; this
redirect is the v0.1.0 fallback.

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
| 11 | Backup filename collision | Operator (unlikely — clock skew or retry loop) |
| 20 | Migration failed | Operator (restore backup — see §9.8) |
| 30 | Built-in type validation failure | Collabhost team (packaging bug) |
| 31 | User-type validation failure | Operator (fix / remove the bad JSON) |
| 40 | Seeder threw unexpectedly (proxy or admin) | Operator (file an issue) |
| 50 | TypeStore built-in resource missing | Collabhost team (packaging bug) |

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
$actual   = (Get-FileHash -Algorithm SHA256 collabhost-0.1.0-win-x64.zip).Hash.ToLower()
if ($expected -ne $actual) { throw 'Checksum mismatch.' } else { 'OK' }
```

The install scripts do this automatically — failure to match aborts the
install before extraction.
