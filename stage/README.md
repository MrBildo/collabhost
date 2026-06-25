# Stage deploy kit (card #443)

The Collabhost-repo half of the disposable **stage** environment: the on-box
build-from-ref deploy script, the curated demo seed, and the told-inputs contract.
The box half (stage user + paths + systemd unit, the SSH forced-command dispatcher,
per-bot keys, the scoped sudoers, Caddy edge `:8080`/`:8443` + internal CA) is Theo's
one-time stand-up — see `stage-box-access-design.md` / `stage-env-it-design.md`.

Stage is a second, fully isolated Collabhost on the `collabhost` box, redeployed
self-service by the team. The deploy **builds the real release archive from a ref on
the box** (mirroring the prod publish), swaps it into the stage tree, and — by default
— wipes stage state and re-seeds a curated demo set.

## Files

| File | Role |
|---|---|
| `deploy-stage.sh` | The deploy. Build-from-ref → stop → swap → (wipe+seed \| keep) → start → smoke. |
| `seed-demo-apps.sh` | Register-if-absent seed; reads `demo-apps/manifest.json`. Also runnable standalone for a manual re-seed. |
| `lib/stage-common.sh` | Shared helpers: told-inputs loader, the two privileged primitives, the wipe guard. |
| `instance.env.example` | The told-inputs key set the script requires (the reconcile artifact with Theo's stand-up). |
| `demo-apps/` | The curated demo artifacts + the data-driven `manifest.json`. |

## Flag grammar (the dispatcher contract)

The root-owned dispatcher invokes:

```
deploy-stage.sh --ref <branch|tag|sha> [--keep-data]
```

- **`--ref <ref>`** — required. `<ref>` must match `^[A-Za-z0-9][A-Za-z0-9._/-]*$` and
  contain no `..`. The script re-validates this (identical charset to the dispatcher
  allowlist) so it is safe even if invoked directly.
- **`--keep-data`** — optional. Code-only swap: preserve stage state, skip **both** the
  wipe **and** the seed. Absent ⇒ clean deploy (wipe + seed).
- `--dry-run` exists for local authoring only (validate args + told-inputs + the wipe
  guard, print the plan, touch nothing). The dispatcher never passes it.

The script streams its log to stdout/stderr (the dispatcher relays it over SSH) and its
exit code is the deploy's exit code.

### Repo → box path mapping

The kit lives at `stage/` in the repo; Theo's stand-up copies it to
`/opt/collabhost-stage/deploy/` (so the dispatcher runs
`/opt/collabhost-stage/deploy/deploy-stage.sh`). `lib/`, `seed-demo-apps.sh`, and
`demo-apps/` sit beside it. The **build tools** (`tools/build-caddy.sh`,
`tools/compute-wwwroot-hash.sh`) and the **demo artifacts** are taken from the
freshly-checked-out ref at `$STAGE_SRC`, so a ref's changes to those are exercised; the
deploy *kit itself* is pinned at the kit path and refreshed deliberately (the deploy
mechanism is trusted, the thing being built/seeded comes from the ref).

## The two privileged primitives (for the sudoers allowlist)

The deploy runs as `stage-deploy` and needs exactly two sudo verbs — Theo's
`/etc/sudoers.d/stage-deploy` allowlists these and **no prod path / no prod service**:

1. `systemctl {start,stop,restart,status} <STAGE_SERVICE>`
2. `runuser -u <STAGE_USER> -- <argv>`

Every stage-tree file operation (binary/wwwroot swap, config merge, wipe, seed copy)
routes through primitive 2 — i.e. it runs **as the stage service user**, which owns the
stage trees and (by kernel perms) cannot touch prod. The build itself runs unprivileged
as `stage-deploy` in `$STAGE_SRC`; build output is world-readable (no secrets) so the
swap can read it across the user boundary.

> **Stage tree-ownership relaxation (reconcile with Theo).** Because the swap/merge/wipe
> run as `STAGE_USER`, the stage trees it writes — `$STAGE_PREFIX` (incl. `bin/`),
> `$STAGE_CONFIG`'s dir, `$STAGE_DATA_ROOT`, `$STAGE_SRV` — must be **owned by
> `collabhost-stage`**. Prod keeps binaries root-owned (read-only-deploy posture); stage
> relaxes that for self-service. `$STAGE_SRC` is `stage-deploy`-owned but must be
> traversable+readable by `collabhost-stage` (live under `/opt/collabhost-stage/`, 0755
> dirs). The alternative — targeted per-path root sudo rules — is brittle; this keeps the
> sudoers to two rules.

## Build-on-box (mirrors the prod publish)

`build_archive` reproduces `publish.yml`'s pipeline for the box's single RID, then
installs the verified archive:

1. `npm ci` + `vite build` → copy `frontend/dist` into the API's `wwwroot`
2. bundled Caddy — `STAGE_CADDY_SOURCE=build` runs `tools/build-caddy.sh` against the
   ref's `caddy.version` / `xcaddy.version` / `caddy-plugins.txt` (faithful — a Caddy
   bump in the ref is actually tested on stage); `=copy` copies prod's bundled caddy.
3. `tools/compute-wwwroot-hash.sh` (the same script `publish.yml` shells; #342/#395)
4. single-file self-contained `dotnet publish` with `-p:WwwrootHash` and a
   `0.0.0-stage-<sha>` version stamp
5. stage the 8-item archive, `tar` it, and verify the flat 8-item contract by extracting
   it — installs from the verified extract (the operator's real path)

## Wipe safety (three independent layers)

A clean deploy wipes `$STAGE_DATA`, `$STAGE_APP_DATA`, `$STAGE_SRV` (and `$STAGE_CADDY_STORAGE`
only if `STAGE_WIPE_CADDY=1`). It can never reach prod:

1. **The dispatcher's sudoers names no prod path** (Theo).
2. **The wipe runs as `collabhost-stage`** — the kernel denies it every prod byte.
3. **`assert_wipe_target`** (this kit) refuses any path that isn't absolute, carries no
   `collabhost-stage`/`/srv/stage` marker, is shallow/system, isn't under a stage root, or
   equals/contains/is-contained-by a prod anchor (`PROD_DATA`/`PROD_PREFIX`/`PROD_CONFIG_DIR`).

## The seed (data-driven, register-if-absent)

`seed-demo-apps.sh` reads `demo-apps/manifest.json` and registers every app whose slug is
not already present (idempotent — safe to re-run; the default deploy always seeds a freshly
wiped DB). Each app: copy its artifact under `/srv/stage/<slug>` (as the stage user),
optionally build it, `POST /api/v1/apps` (with `X-User-Key`), then best-effort `POST /start`.
An app whose `build` needs a tool that's absent on the box is **skipped with a warning**
(registering an un-built artifact would just 502); no-build apps always register; a real
register HTTP failure is fatal.

The placeholder set covers every distinct dashboard row shape: `dotnet-app` (process +
route + health + .NET probe), `nodejs-app` (process + route + Node probe), `static-site`
(file-server route, no process), `external-route` (route only, no process), `internal-service`
(supervised process, no route). **The final curated selection is a Dana/Bill call** (it feeds
the #440 screenshots) — edit `manifest.json`, no script change needed.

## Smoke-test locally

```
COLLABHOST_STAGE_INSTANCE_ENV=stage/instance.env.example \
  bash stage/deploy-stage.sh --ref main --dry-run
```

`--dry-run` exercises arg parsing, the told-inputs loader, and the wipe guard with no
build, no sudo, no HTTP — it prints the plan and the exact privileged commands it would run.

## Open questions

- **Final demo-app curation** (Dana/Bill) — the exact set + count, incl. whether to add an
  `executable` demo. The manifest is the only thing to change.
- **Caddy `build` vs `copy`** — default is `build` (faithful to the task's "mirror the prod
  publish"); it needs **Go on the box** for `xcaddy`. If Go is not present, set
  `STAGE_CADDY_SOURCE=copy` at stand-up (the script fails loud pointing at this). Reconcile
  with Theo on whether the box carries Go.
- **Demo runtime deps** — the `nodejs-app` demo needs `node`/`npm` on the box; absent ⇒ it's
  skipped (warn). The `.NET` demos use the box's SDK 10. Confirm the box's available runtimes.
- **Stage tree ownership** — the relaxation noted above (stage trees `collabhost-stage`-owned;
  `$STAGE_SRC` readable by the stage user) — reconcile with Theo's stand-up.
- **`instance.env` key set** — `instance.env.example` is the proposed contract; reconcile the
  exact keys/paths against Theo's stand-up.
