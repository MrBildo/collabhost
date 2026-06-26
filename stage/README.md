# Stage deploy kit (card #443)

The Collabhost-repo half of the disposable **stage** environment: the on-box
build-from-ref deploy script, the curated demo seed, and the told-inputs contract.
The box half (stage user + paths + systemd unit, the SSH forced-command dispatcher,
per-bot keys, the scoped sudoers, the root-owned `stage-privop` helper, Caddy edge
`:8080`/`:8443` + internal CA) is Theo's one-time stand-up — see card #443 for the
ratified design + the dispatcher↔script contract.

Stage is a second, fully isolated Collabhost on the `collabhost` box, redeployed
self-service by the team. The deploy **builds the real release output from a ref on
the box** (mirroring the prod publish), installs it into the stage tree via the
privileged helper, and — by default — wipes stage state and re-seeds a curated demo
set. Base domain `stage.collab.internal`; the edge binds `:8080`/`:8443` on the
internal CA (decision #2).

## Files

| File | Role |
|---|---|
| `deploy-stage.sh` | The deploy. Build-from-ref → stop → install → (wipe+seed \| keep) → start → smoke. |
| `seed-demo-apps.sh` | Build (unprivileged) + `seed-install` + register-if-absent; reads `demo-apps/manifest.json`. Also runnable standalone. |
| `lib/stage-common.sh` | Shared helpers: told-inputs loader, the single privileged primitive (`privop`), the wipe guard. |
| `instance.env.example` | The told-inputs key set the script requires (the reconcile artifact with Theo's stand-up). |
| `demo-apps/` | The curated demo artifacts + the data-driven `manifest.json`. |

## Flag grammar (the dispatcher contract)

The root-owned dispatcher invokes:

```
deploy-stage.sh --ref <branch|tag|sha> [--keep-data]
```

- **`--ref <ref>`** — required. `<ref>` must match `^[A-Za-z0-9][A-Za-z0-9._/-]{0,199}$`
  and contain no `..`. The script re-validates this (byte-identical charset to the
  dispatcher allowlist) so it is safe even if invoked directly. The dispatcher accepts
  both `--ref X` and `--ref=X` and always passes `--ref X` to the script.
- **`--keep-data`** — optional. Code-only swap: preserve stage state, skip **both** the
  wipe **and** the seed. Absent ⇒ clean deploy (wipe + seed).
- `--dry-run` exists for local authoring only (validate args + told-inputs + the wipe
  guard, print the plan, touch nothing). The dispatcher never passes it.

The script streams its log to stdout/stderr (the dispatcher relays it over SSH) and its
exit code is the deploy's exit code.

### Repo → box path mapping

The kit lives at `stage/` in the repo; Theo's stand-up copies it to
`/opt/collabhost-stage/deploy/` (so the dispatcher runs
`/opt/collabhost-stage/deploy/deploy-stage.sh`). The deploy checks out the ref to
`/home/stage-deploy/build/checkout` and publishes to `/home/stage-deploy/build/publish`
— the build-output paths Theo's `install-artifacts` / `install-caddy` verbs read. The
**build tools** (`tools/build-caddy.sh`, `tools/compute-wwwroot-hash.sh`) and the **demo
artifacts** come from the freshly-checked-out ref, so a ref's changes to those are
exercised; the deploy *kit itself* is pinned at the kit path and refreshed deliberately.

> **Kit ownership is `root:root` (a hard invariant — Theo's stand-up owns it).** The
> dispatcher `exec`s `/opt/collabhost-stage/deploy/deploy-stage.sh` only after a tamper
> check that the script is **uid 0 and not group/other-writable** (`stat -c '%u' == 0`).
> So the kit-install step **`chown -R root:root /opt/collabhost-stage/deploy`** after
> copying `stage/*` in. A non-root owner (it drifted three times when laid out-of-band)
> makes the dispatcher refuse **every** deploy with `deploy-stage.sh ... failed the tamper
> check` — a confusing failure mode for a permissions slip. This is now **enforced in
> `stage/box/stage-standup.sh`** (repo-tracked, #445): re-run the stand-up with the repo
> `stage/` dir as its 2nd arg to (re)lay the kit root-owned, rather than copying it in
> out-of-band.

## The privileged helper (`stage-privop`)

Theo's box-half deliberately **did not** build a direct-`sudo systemctl/install/chown/find`
sudoers — wildcard path args there are `..`-traversal-exploitable toward prod. Instead all
privilege routes through one root-owned helper that **hardcodes every stage path**, so no
path and no wildcard ever crosses the sudo boundary:

```
sudo /opt/collabhost-stage/deploy/stage-privop <verb>
```

Verbs (the helper owns each verb's hardcoded paths + exact behavior):

| Verb | Used for |
|---|---|
| `install-artifacts` | Install the built `collabhost` binary + `wwwroot/` + docs from the publish dir into the stage prefix. |
| `install-caddy` | Install the bundled `caddy` from the publish dir. |
| `wipe-data` | Clear stage state (`$STAGE_DATA`, `$STAGE_APP_DATA`, `$STAGE_SRV`). |
| `wipe-ca` | Clear the internal CA storage (only when `STAGE_WIPE_CADDY=1`). |
| `seed-install` | Copy the built demo trees from `<checkout>/stage/demo-apps` into `/srv/stage`. |
| `start \| stop \| restart \| status` | Service control. |
| `logs [N]` | Read the stage service log (the dispatcher's `stage-logs` read verb). |

The build itself runs **unprivileged** as `stage-deploy` in the checkout; build output is
world-readable (no secrets) so the privileged install verbs can read it. **App registration
is not privileged** — it is an HTTP `POST /api/v1/apps` to the local control plane
(`http://127.0.0.1:58500`) with the stage admin key read from a 0600 file
(`/home/stage-deploy/.stage-admin-key`; v1.8.0 requires the Administrator role, which it
carries).

> **Stage config is owned by the stand-up, not merged per deploy.** The verb menu carries
> no config-merge verb; the stage config is the unit's env block + a static appsettings
> Theo provisions once. A ref that ships a new appsettings key the stage config needs would
> want a new `stage-privop` verb (Theo's call) — see card #443.

> **Stage tree-ownership relaxation (Theo's half).** Because the install/wipe/seed verbs run
> as the `collabhost-stage` service user, the stage trees (`$STAGE_PREFIX` incl. `bin/`, the
> config dir, `$STAGE_DATA_ROOT`, `$STAGE_SRV`) are owned by `collabhost-stage`. Prod keeps
> binaries root-owned (read-only-deploy posture); stage relaxes that for self-service. The
> build checkout under `/home/stage-deploy/build` is `stage-deploy`-owned.

## Build-on-box (mirrors the prod publish)

`build_archive` reproduces `publish.yml`'s pipeline for the box's single RID into
`/home/stage-deploy/build/publish`, then verifies it:

1. `npm ci` + `vite build` → copy `frontend/dist` into the API's `wwwroot`
2. bundled Caddy — `STAGE_CADDY_SOURCE=build` runs `tools/build-caddy.sh` against the
   ref's `caddy.version` / `xcaddy.version` / `caddy-plugins.txt` (faithful — a Caddy
   bump in the ref is actually tested on stage); `=copy` copies prod's bundled caddy
3. `tools/compute-wwwroot-hash.sh` (the same script `publish.yml` shells; #342/#395)
4. single-file self-contained `dotnet publish` (with `-p:WwwrootHash` and a
   `0.0.0-stage-<sha>` version stamp) directly into the publish dir
5. assemble + verify the publish contract (`collabhost`, `caddy`, `wwwroot/`,
   `wwwroot.sha256`, docs/licenses) — `install-artifacts` / `install-caddy` read this dir

## Wipe safety (three independent layers)

A clean deploy clears `$STAGE_DATA`, `$STAGE_APP_DATA`, `$STAGE_SRV` (and the internal CA
only if `STAGE_WIPE_CADDY=1`). It can never reach prod:

1. **The dispatcher's sudoers names only `stage-privop`** — no prod path, no wildcard file
   op (Theo).
2. **The `wipe-data` / `wipe-ca` verbs hardcode the stage dirs and run as `collabhost-stage`**
   — the kernel denies the stage user every prod byte (Theo's helper).
3. **`assert_wipe_target` pre-flight** (this kit) — before invoking the path-less wipe verb,
   the deploy validates the instance.env-declared stage dirs (which must mirror the helper's
   hardcoded paths), refusing any path that isn't absolute, carries no
   `collabhost-stage`/`/srv/stage` marker, is shallow/system, isn't under a stage root, or
   equals/contains/is-contained-by a prod anchor (`PROD_DATA`/`PROD_PREFIX`/`PROD_CONFIG_DIR`).
   This catches a misconfigured told-input in-script; the load-bearing wall is layers 1–2.

## The seed (data-driven, register-if-absent)

`seed-demo-apps.sh` reads `demo-apps/manifest.json` and seeds every app whose slug is not
already present (idempotent — safe to re-run). It (1) builds each app's artifact
**unprivileged** in the checkout (skipping — warn, continue — any whose `requires` tool is
absent **or whose build command fails**; an un-built artifact would just 502, and one bad
demo build must not abort an otherwise-green deploy), (2) runs `privop seed-install` once to
copy the built demo trees into `/srv/stage/<slug>` (as the stage user), then (3)
`POST /api/v1/apps` per app and a best-effort `POST /start`. A real register HTTP failure is
fatal. The closing summary reports `registered` / `already-present` / `skipped-toolchain` /
`skipped-build` counts and names the skipped demos. **The demo source dir name must equal the
slug** (seed-install preserves dir names; the artifact path resolves to
`/srv/stage/<slug>`) — the seed asserts this.

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
build, no sudo, no HTTP — it prints the plan and the exact `stage-privop` verbs it would
invoke. (Linux/WSL: the guard uses GNU `realpath -m`.)

## Open questions (first-live-deploy seams for Theo)

- **`install-caddy` source path — RESOLVED (#445).** `stage-privop install-caddy` now
  installs `${PUBLISH}/caddy` (the freshly-built, ref-pinned caddy), falling back to prod's
  bundled caddy only if no built caddy is present — so `=build` exercises the ref's
  `caddy.version` pins and `=copy` still lands prod's caddy (the same binary at that path).
  The deploy-side `=build` warn that compensated for the divergence has been dropped (Kai's
  #319 K-1 removal trigger met). Helper source: `stage/box/stage-privop`.
- **`seed-install` copy semantics** — the deploy assumes it copies
  `<checkout>/stage/demo-apps/*` → `/srv/stage/*` (dir name == slug). Confirm against the
  helper.
- **Config merge dropped** — confirm the stand-up's env block + static appsettings is the
  whole stage config (no per-deploy merge), or add a config verb.
- **Final demo-app curation** (Dana/Bill) — the exact set + count. Edit `manifest.json` only.
- **Box toolchain** — `STAGE_CADDY_SOURCE=build` needs Go; the `nodejs-app` demo needs
  `node`/`npm`; the `.NET` demos need a .NET 10+ SDK. Each fails / skips loud if absent —
  and a demo whose build *fails* is now skipped-with-warning too (not fatal), so one bad
  demo build no longer aborts an otherwise-green deploy. The `.NET` demos carry their own
  `stage/demo-apps/global.json` (permissive `rollForward`) so they build on any recent SDK,
  independent of the product's pinned SDK (a lagging box SDK no longer breaks the seed).
