# Stage box half (cards #443 / #445)

The **box-side** artifacts for the disposable stage Collabhost instance — the half
Theo (IT) stands up on the `collabhost` box, complementary to the deploy kit in `stage/`
(Remy's). These were box snowflakes (held in a gitignored scratch dir) until **#445**
version-controlled them here, so the stand-up is reproducible from source.

| File | What it is | Installed to (by `stage-standup.sh`) |
|---|---|---|
| `stage-standup.sh` | Idempotent box provisioner: users, dir tree, the unit, appsettings, the dispatcher, sudoers, the deploy kit (root:root), the privop helper. | runs in place |
| `collabhost-stage.service` | systemd unit for the stage instance (high ports, non-root, isolated `-stage` footprint, no shared state with prod). | `/etc/systemd/system/` |
| `stage-appsettings.json` | Static stage config (`:8080/:8443`, `BaseDomain=stage.collab.internal`, app API `:58500`). `AdminKey` is `null` here — the real key is a 0600 systemd drop-in. | `/etc/collabhost-stage/appsettings.json` |
| `stage-privop` | The **privilege boundary**: the single root-owned helper `stage-deploy` may run as root, with a fixed verb menu and hardcoded stage-only paths (no caller path crosses sudo). | `/opt/collabhost-stage/deploy/stage-privop` |
| `stage-deploy-dispatch` | The **SSH command boundary**: the forced command on every `stage-deploy` key; validates `$SSH_ORIGINAL_COMMAND` against a strict allowlist (`deploy-stage` / `stage-logs` / `stage-status`), never eval'd. | `/usr/local/bin/stage-deploy-dispatch` |
| `stage-deploy.sudoers` | Grants `stage-deploy` exactly one privilege: run `stage-privop` as root — nothing else. | `/etc/sudoers.d/stage-deploy` (validated with `visudo -c`) |
| `install-admin-key.sh` | Generates + installs the stage admin key (ULID): a 0600 systemd drop-in for the service + a stage-deploy-readable copy for the seed. Disposable-stage secret, **not** a prod credential. | run separately (so the generated key can be captured) |

## Stand-up

```
sudo ./stage-standup.sh <staging-dir> [<repo-stage-dir>]
```

`<staging-dir>` holds these config files (point it at this `stage/box/` dir, or a copy).
Pass the repo `stage/` dir as the **2nd arg** to (re)lay the deploy kit (Remy's
`deploy-stage.sh` + `lib/` + `seed-demo-apps.sh`) into `/opt/collabhost-stage/deploy/`
and `chown -R root:root` it — the ownership the dispatcher's tamper check requires. This
is the **single place** that owns kit-install; do **not** copy the kit in out-of-band
(that drifted the ownership repeatedly). The `install-admin-key.sh` secret step runs
separately so the generated key can be captured.

> The internal-CA root cert (`stage-internal-ca.crt`) is **generated** on the box (Caddy
> `tls internal`), so it is intentionally **not** version-controlled here — it is output,
> not source, and `stage-privop wipe-ca` regenerates it.
