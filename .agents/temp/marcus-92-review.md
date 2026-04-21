# Marcus review — PR #92 (#153 Phase 4a, release workflow)

**Branch:** `feature/153-04a-publish-workflow` @ `648c7d4`
**Spec:** `.agents/specs/release-pipeline.md` (R2.1, merged to `main`)
**Scope:** CI/infrastructure. Zero runtime code changes verified.

## Verdict

**Ship-with-fixes.**

Two HIGH items need attention before first real release cut — both are latent bugs that the workflow will hit on the first run, not spec-reconciliation niceties. Four MED items. Several LOWs and spec-drift reconciliations to fold into a follow-up doc commit.

The overall shape is solid. The four-job graph is right, the SHA-512 call is the correct one, native runners are defensible, and the implementation is tight. My concerns are about what happens when the workflow executes on day 1 — which, per Remy's own gap #2, has not been exercised — and a few archive-contents hygiene items that will embarrass us on public download.

---

## Strong positives

- **Job graph matches the spec exactly.** `extract-version → build-frontend → build-matrix (5 legs, fail-fast: false) → publish-checksums`. Fails fast on tag regex before any dotnet/node work.
- **SHA-512 for upstream Caddy verify is the right call.** Spec was wrong; the implementation is right (see Spec-drift #1).
- **`set -euo pipefail` on every bash block.** Pipeline failures propagate, unset vars fail, errors exit non-zero. Good discipline; this is the shell hygiene I'd expect.
- **`awk '$2 == name {print $1}'` for checksum lookup.** Avoids GNU-vs-BSD grep differences. This kind of portability-aware detail is exactly what separates "worked in CI once" from "works on every runner's Caddy release asset". Well done.
- **Caddy verify is correctly gated BEFORE extraction** (lines 145-160 precede the `case` extract at 166-173). If the SHA mismatches, extraction never runs, `exit 1` propagates via `pipefail`, and the matrix leg fails hard. This answers two of my checklist items in one design.
- **Per-leg + aggregated checksum split.** Manual downloaders get `<archive>.sha256` next to the archive on the Release; scripted consumers get `checksums.txt` — both are produced from the same source (the per-leg artifact upload). No drift possibility.
- **`--clobber` on every `gh release upload`.** Operationally the right call for this workflow shape (see below for the one case where I'd want it to NOT clobber).
- **Archive-filename convention honored.** `collabhost-{version}-{rid}.{ext}`, no `v` prefix. Dana's R1 ruling applied correctly everywhere.

---

## Issues

### HIGH-1 — PDB + static-webassets manifest will ship inside the archive

`dotnet publish -c Release -r ${RID} --self-contained -p:PublishSingleFile=true` produces a directory that contains, at minimum:

- `collabhost[.exe]` — the single-file wrapper (correct, shipped)
- `collabhost.pdb` — unbundled debug symbols (~10-50 MB); `PublishSingleFile` does NOT fold the PDB into the single-file by default — it stays alongside
- `appsettings.json` — content file (correct, shipped per spec §5.1)
- `Collabhost.Api.staticwebassets.endpoints.json` (or similar manifest) — the static web assets endpoint map

The `Stage archive` step (line 198) does not scrub the staging directory — it assumes the publish output already matches the shipping contract. Then the `Archive (tar.gz)` and `Archive (zip)` steps glob `*` / `.` from the stage directory, so whatever `dotnet publish` emits goes into the archive.

**What the operator will see on first release:**
- An 80-130 MB `collabhost.pdb` alongside `collabhost` in the extracted archive. Looks sloppy in a v0.1.0 release; also bloats the archive by ~30 MB compressed.
- A `*.staticwebassets.endpoints.json` file sitting at the root of the archive. This is a publish implementation artifact that has no place in a distributed binary.

**Spec alignment:** §5.1 lists exactly six items (collabhost, caddy, appsettings.json, INSTALL.md, LICENSES/caddy-LICENSE, LICENSES/caddy-NOTICE). The archive will currently ship ≥8. Spec §5.4 ("What's explicitly NOT in the archive") does not enumerate PDB or the SWA manifest — it should, but the bigger issue is that the implementation doesn't filter them.

**Fix options (pick one — I mildly prefer A):**

- **A. Suppress at publish time.** Add `-p:DebugType=embedded -p:DebugSymbols=false` to the publish command (or just `-p:DebugType=embedded` to fold the PDB into the single-file). For the SWA manifest, `-p:StaticWebAssetsEnabled=false` at publish time; I want to verify this doesn't break the embedded SPA, but the SPA files should already be in `wwwroot/` from the frontend artifact and the fallback route. An alternative: `-p:PublishStaticWebAssets=true` stays on but `<StaticWebAssetsPublishManifest>false</StaticWebAssetsPublishManifest>` in csproj.
- **B. Scrub at stage time.** After the `cp` lines in `Stage archive`, add explicit `rm -f "${STAGE}/${{ matrix.rid == 'win-x64' && 'collabhost.pdb' || 'collabhost.pdb' }}" "${STAGE}"/*.staticwebassets.endpoints.json`. Works, but feels like compensating for the publish emitting the wrong set.
- **C. Explicit allow-list copy.** Re-stage into a clean `archive-staging/` directory, `cp` only the six spec'd items. This is the most defensible because the contract is enumerated in the staging step itself, and any future publish surprise (e.g., SDK emits a new manifest) doesn't leak into the archive.

**Why HIGH:** This is what operators see on day 1. An 80 MB mystery `.pdb` in a v0.1.0 archive is the kind of thing that makes a public download look unserious. It's a two-line fix, and we catch it now or we catch it after 1.0 ships.

---

### HIGH-2 — First-run validation gap + no `actionlint` in CI means spec-to-YAML bugs hide until the first release

Remy's PR body (gap #2) correctly notes the workflow can't be exercised without a Release, and the strict tag regex (`^v[0-9]+\.[0-9]+\.[0-9]+$`) rejects sacrificial pre-release tags like `v0.0.1-alpha.0`. Card #157 is filed for a proper dry-run workflow.

Separately, Remy notes `actionlint` didn't run locally, so he walked the YAML "logically." I believe him — but the composition of "never executed" + "no lint" means any typo or reference bug in the workflow surfaces on `v0.1.0` Release creation. That's the worst possible time to find out that, say, a matrix variable name is misspelled.

**What I want before we merge 4a:**

1. **Add `actionlint` to CI.** A ~6-line job on `ci.yml` that runs `rhysd/actionlint` against `.github/workflows/*.yml` on PR. This doesn't block the current PR — we can land it as a sibling PR — but I don't want Phase 4b to ship before `actionlint` is running. This is the kind of catch that a static check gets for free and no amount of careful YAML reading will reliably catch. Remy explicitly called out that he couldn't run it locally; the fix is to run it in CI.

2. **Dry-run path matters more than #157 suggests.** Option 2 from Remy's PR body ("sacrificial `v0.0.1-alpha.0` tag") is not the right pattern — mutating the tag regex on a throwaway branch to test the workflow means we're testing a *different* workflow. The right dry-run is `workflow_dispatch: {}` added to `publish.yml` itself, guarded to skip the release-upload steps when `github.event_name != 'release'`. That's what #157 should produce. Pre-merge it would derisk 4a significantly.

**Why HIGH:** Shipping Phase 4a means the next release cut blindly exercises ~300 lines of untested bash-and-YAML on five platforms simultaneously, with `gh release upload --clobber` writing into a public GitHub Release. Fail-fast plus matrix re-run gives us recovery room, but it's recovery *in public view*. Minimum derisking: actionlint in CI before 4a merges. Better: #157 dry-run workflow before 4a merges.

I'm not asking to block the PR on #157 — but I am asking to land actionlint in CI first (or concurrently) because it's cheap and the failure mode is so visible.

---

### MED-1 — `Compress-Archive` behavior on Windows zip is different from `tar -czf`, and the difference is load-bearing

The Windows archive step:

```yaml
Compress-Archive -Path "publish/collabhost-$env:VER-${{ matrix.rid }}/*" `
                 -DestinationPath "collabhost-$env:VER-${{ matrix.rid }}.zip"
```

The Linux/macOS archive step:

```bash
tar -czf "collabhost-${VER}-${{ matrix.rid }}.tar.gz" \
  -C "publish/collabhost-${VER}-${{ matrix.rid }}" .
```

**Two functional differences to flag:**

1. **Directory prefix.** `tar -C dir .` produces entries like `./collabhost`, `./LICENSES/caddy-LICENSE`. `Compress-Archive -Path "dir/*"` produces entries like `collabhost`, `LICENSES/caddy-LICENSE`. On extraction, both work — but `tar -C dir .` with modern `tar` produces a flat top-level (what we want); older `tar` on macOS historically produced `./` prefixes. Not a bug, but install scripts (Phase 4b) need to handle both shapes or normalize extraction. Worth a follow-up note in Phase 4b's install script spec.

2. **Unix permission bits.** `Compress-Archive` does not preserve or set Unix execute bits. Irrelevant on Windows (the archive is a zip extracting to NTFS; `.exe` is executable by extension). But if someone ever downloads a `.zip` on Linux (unlikely for win-x64 but happens), the extracted `caddy.exe`/`collabhost.exe` won't be executable. We don't ship `.zip` for Linux targets, so this is academic — I'm noting it for completeness.

**More concerning:** `tar -czf` on the Unix legs. `chmod +x` is called on the downloaded Caddy binary (line 175) but there is no corresponding `chmod +x "${STAGE}/collabhost"` after the `cp` of `caddy-download/...`. The `dotnet publish` output for self-contained Linux / macOS *should* already have the execute bit set on the binary — but let me be explicit: I haven't personally verified this on .NET 10 SDK for all three Unix RIDs. Single-file publish on Linux historically sets the execute bit; I'd want to see `chmod +x` applied defensively on the Unix legs to the `collabhost` binary too. Cost: one line. Benefit: belt-and-suspenders on a thing that matters (Caddy binary you downloaded got `chmod +x`; why not the Collabhost binary that came out of our own build?).

**Fix:** In the `Stage archive` step, after the Caddy `cp`, add for Unix legs:

```bash
if [[ "${{ matrix.rid }}" != win-x64 ]]; then
  chmod +x "${STAGE}/collabhost"
fi
```

Or, simpler: `chmod +x "${STAGE}/collabhost" 2>/dev/null || true` (mirrors the Caddy-chmod pattern).

---

### MED-2 — `gh release upload --clobber` is correct for matrix re-runs, but silently masks a class of supply-chain bug

`--clobber` means "if this asset already exists on the release, delete it and upload the new one." For matrix re-runs (leg 3 failed, re-run just leg 3) this is exactly what we want.

But consider: an operator publishes a Release, the workflow runs, all five archives upload and `checksums.txt` uploads. A month later, the operator re-publishes the same tag (bad practice, but possible) OR an attacker obtains the ability to trigger the workflow on a stale tag. `--clobber` silently replaces the prior assets, and the only external signal is the GitHub Release's asset modified-time.

**Mitigation options (ranked by simplicity):**

- **A.** Add a preflight check: `gh release view $TAG --json assets | jq '.assets[] | select(.name == "'${ARCHIVE}'")'`. If the asset exists AND the workflow is not a manual re-run (detect via some env), fail. This is complex, has edge cases with `rerun_attempt`, and adds plumbing.
- **B.** Do nothing. `--clobber` is standard; the threat model ("attacker who can trigger the workflow on a stale tag") requires compromise of repo-level secrets, at which point the release story is already lost.
- **C.** Document the behavior in the release cycle playbook (§16 of the spec). "Re-publishing a tag replaces existing archives without warning; re-cut to `v{X}.{Y}.{Z+1}` rather than reusing a tag."

**My recommendation:** C. Ship as-is, document the behavior in the spec's §16 playbook, and note that the mitigation for the "re-published tag" case is to never re-publish tags. This is the community-standard posture and fighting it costs more than it buys. Not blocking.

---

### MED-3 — `retention-days: 1` on the per-leg checksum artifact is tight

The `publish-checksums` job runs after all five matrix legs finish. If the last leg takes ~7-10 minutes and a matrix-wide issue causes one leg to retry over multiple hours (unlikely, but I've seen GitHub do weirder things), the earliest-uploaded checksum artifacts could approach their TTL. `retention-days: 1` is the minimum GitHub allows for `actions/upload-artifact@v4`, but the spec didn't explicitly call out the TTL choice.

More practically: if `build-matrix` finishes successfully and then `publish-checksums` itself fails (aggregation or upload error), a manual re-run of `publish-checksums` would need the per-leg artifacts *still present*. If the failure was discovered >24 hours later, they're gone.

**Recommendation:** Bump to `retention-days: 7` on the checksum artifacts. Zero cost, gives a week of recovery window for `publish-checksums` aggregation issues. Keep `retention-days: 1` on the frontend artifact — it's larger (~5-10 MB) and only needed within the same run.

---

### MED-4 — The `Archive (tar.gz)` step includes `./` entries (cosmetic) AND will include any future stray files in the publish directory

As noted in MED-1, `tar -C dir .` globs everything in `dir`. If a future SDK or plugin emits an unexpected file into `publish/collabhost-${VER}-${{ matrix.rid }}/` — say, `.dotnet-trace.json`, a scratch file, a SourceLink-generated manifest — it ships.

**Related to HIGH-1 fix.** If we do option C from HIGH-1 (explicit allow-list re-stage), this goes away. If we do option A (suppress at publish time), there's a residual risk that an unrelated SDK update adds a new emitted file. The explicit-allow-list approach is the most defensible long-term posture.

I'd accept A as the minimum viable fix for v0.1.0 and carry C as a follow-up card. But the tension is real — the archive contract is "six items"; the implementation ships "whatever `dotnet publish` emits minus what we happen to know to suppress."

---

## LOW

### LOW-1 — `chmod +x "${{ matrix.caddy_bin }}" || true` on the Caddy extract is silently swallowing error codes that might matter

`|| true` under `set -e` is the pattern to opt out of pipefail for a specific command. Used here because Windows doesn't need chmod. Fine — but an opinionated cleanup would be:

```bash
if [[ "${{ matrix.rid }}" != win-x64 ]]; then
  chmod +x "${{ matrix.caddy_bin }}"
fi
```

Now we know a chmod failure on a Unix RID is actually a failure, not a shrug. Very minor.

### LOW-2 — `caddy.version` file ends with a newline that gets stripped correctly, but the stripping is inconsistent

Line 119: `VER=$(cat caddy.version | tr -d '[:space:]')`. Good — strips all whitespace including the trailing newline.

The spec §6.1 shows `VER=$(cat caddy.version)` which would carry the newline and produce `curl` URLs with embedded `%0A`. Remy's implementation is better than the spec here. Consider mentioning this in the spec reconciliation as a clarifying fix.

### LOW-3 — No `timeout-minutes` on matrix legs

`ci.yml` sets `timeout-minutes: 10` on the smoke-tests job. `publish.yml`'s matrix legs inherit the GitHub default (360 min / 6 hours). A hung Caddy download with `curl --retry 3 --retry-delay 5` would eventually surface, but six hours is a long time to leave an orphaned runner running up minutes on a bad day.

**Recommendation:** `timeout-minutes: 30` on `build-matrix`. `timeout-minutes: 15` on the other three jobs. This is cheap operational hygiene and matches `ci.yml`'s pattern.

### LOW-4 — Shell/script style inconsistency: mixing `${VER}` and `${{ matrix.rid }}` in the same bash heredoc

```bash
ARCHIVE="collabhost-${VER}-${{ matrix.rid }}.${{ matrix.ext }}"
```

GitHub Actions expression substitution (`${{ }}`) happens before the shell runs. `${VER}` is shell-var substitution happening inside the shell. So this works — but it means half the variables come from the runner environment and half from the GA expression layer.

Reading at a glance, this looks like the shell is dereferencing all four `${}` patterns uniformly. It isn't. The `${{ matrix.* }}` values are baked into the script text by GA before execution.

**Tactical fix:** Promote `matrix.rid` and `matrix.ext` to step-level `env:` blocks for the bash steps, so the whole script reads as uniform shell variables:

```yaml
env:
  RID: ${{ matrix.rid }}
  EXT: ${{ matrix.ext }}
  VER: ${{ needs.extract-version.outputs.version }}
run: |
  ARCHIVE="collabhost-${VER}-${RID}.${EXT}"
```

Easier to read, easier to `echo` for debugging, avoids the mixed-substitution mental load. Not blocking — style preference.

### LOW-5 — The workflow doesn't explicitly verify the published archive contract

After the `Archive` step, there's no step that checks "did this archive actually contain what we expected?" — e.g., `tar -tzf collabhost-... | sort` to dump the entries, or a guarded `test -f` sequence asserting each of the six D8 items is present.

For a first-run workflow with no dry-run capability (see HIGH-2), adding a "verify archive contents" step as step 6.5 — before upload — would:

- Produce diagnostic output in the GA log for each leg showing what's actually inside.
- Fail fast if `dotnet publish` emits an unexpected tree (catches HIGH-1 if we don't go with option C).
- Make the archive contract self-verifying.

```bash
# Verify archive contract
case "${ARCHIVE}" in
  *.zip)    unzip -l "${ARCHIVE}" ;;
  *.tar.gz) tar -tzf "${ARCHIVE}" | sort ;;
esac
```

Not blocking. Would pay for itself the first time we run it.

---

## Questions for Remy

1. **PDB emission confirmation.** Did you verify by local `dotnet publish` what actually lands in the publish directory on one RID? If yes, was PDB present, and if so did you consider scrubbing? If no, can we run it locally on `win-x64` as a sanity check before merge? (HIGH-1)
2. **actionlint path forward.** Are you comfortable landing an `actionlint` job in `ci.yml` in a sibling PR before 4a merges, or would you rather tag along? (HIGH-2 mitigation)
3. **`chmod +x collabhost` on Unix legs.** Do you have a reason you did `chmod` on Caddy but not on our own binary? If "because it's already +x", I'd like to understand how you verified that for all three Unix RIDs. (MED-1)
4. **`caddy-NOTICE` — upstream alignment.** The shipped NOTICE is two lines. I pulled `github.com/caddyserver/caddy/blob/v2.11.2/NOTICE` and got the same two lines, so this is correct. Worth a sanity-check entry in a "bump Caddy" runbook (spec §6.3 mentions this). Not a PR question — a spec question. (LOW, confirming.)
5. **Why `retention-days: 1` on the frontend artifact?** I'm fine with it (only needed within the same run), but confirm you considered the re-run scenario. (Informational.)
6. **Did you cross-check the Caddy asset-filename pattern for Windows?** The spec §3.6 shows `caddy_{CADDY_VER}_windows_amd64.zip`. Your matrix row has `caddy_os: windows`, `caddy_arch: amd64`, `caddy_ext: zip`, which produces `caddy_2.11.2_windows_amd64.zip`. This matches `github.com/caddyserver/caddy/releases/download/v2.11.2/` contents. Confirmed. (Noting for audit trail.)

---

## Spec-drift items (reconciliation commits)

File these as a single doc-only follow-up PR against `main` after this PR lands. Order matches the spec sections.

1. **§6.2 — "No checksum verification on the Caddy download (in v1)."** False as of 4a implementation. Rewrite this bullet to describe the SHA-512 verification step including algorithm justification (upstream publishes SHA-512; we match upstream's published integrity guarantee; our own archive integrity is SHA-256 per locked decision 9, which is a different trust domain).
2. **§6.2 — URL template.** Spec shows `curl` call before extract; implementation adds a second `curl` for the checksums file and verification between them. Document the three-step flow (download asset, download checksums, verify, extract).
3. **§17.1 — Risks table.** "Caddy download from GitHub fails mid-workflow" risk stays, but add a new row: "Caddy download passes but upstream checksum file is corrupted / missing / tampered" with mitigation "SHA-512 verify; mismatch fails leg; clear error message."
4. **§5.4 — "What's explicitly NOT in the archive."** Add `collabhost.pdb` and `*.staticwebassets.endpoints.json` to the list. This documents the hygiene contract regardless of whether HIGH-1 is fixed via publish-flag or stage-filter — the *contract* is "this is not in the archive," and the implementation enforces the contract.
5. **§6.1 — `caddy.version` reading.** Add note: "The version string is whitespace-stripped on read (`tr -d '[:space:]'`) to handle editors that add a trailing newline."
6. **Appendix A — "SHA256 upstream verification."** Update to SHA-512 for upstream, SHA-256 for our archives. Keep the two algorithms explicitly distinguished.
7. **Implementation plan §Phase 4a — "Review gate: Marcus for workflow architecture."** Add post-hoc: "Review completed on PR #92 with ship-with-fixes verdict; HIGH-1 (archive hygiene), HIGH-2 (first-run derisking) addressed before Phase 5 cut." (This is housekeeping — makes the spec an honest record of what happened.)

---

## Convention violations

None in the YAML itself. The `ci.yml` sibling workflow uses the same `actions/*@v4` versions, same `dotnet-version: '10.0.x'`, same `node-version: '22'`. Good parity.

One soft convention nit: `ci.yml` uses `timeout-minutes` on the `smoke-tests` job. `publish.yml` has no explicit timeouts anywhere (see LOW-3).

---

## Scope notes

- **Scope fences honored.** No installer scripts, no INSTALL.md content, no runtime code changes. Diff against `main` on backend/frontend is empty, confirmed.
- **Phase 4b dependencies clean.** Nothing in this PR pre-commits a decision that 4b would want to revisit. Good shape for handoff.
- **Phase 5 dependencies clean.** `install.sh`/`install.ps1` go into `docs/` per spec §10; they're not in this PR. `checksums.txt` shape matches the install-script verification pattern in §9.5.
- **Card #157 (dry-run workflow) relationship.** My HIGH-2 ask is that actionlint lands before 4a merges; I'm NOT asking that #157 be implemented first. But if Bill wants to be maximally conservative, #157 before the v0.1.0 tag is the cleanest posture.

---

## What would flip the verdict

If Remy addresses HIGH-1 (archive hygiene) and HIGH-2 (actionlint in CI — either in this PR or a sibling PR merging first), this is a clean **ship**.

The MEDs are worth a pass but none of them are release-blocking — they're operational hygiene we'd want before we have five public Releases and wish we'd done them earlier.

The spec drift items are a separate reconciliation commit I'm happy to own or coordinate with Remy on after 4a merges. Spec should be an honest record of what shipped.

---

*Marcus*
*2026-04-20*
