# Release Pipeline -- Spec

**Card:** #153 -- Implement "Release"
**Author:** Remy (backend lead)
**Status:** R2 -- revised post-#156/#159/#158 design work. Ready for Marcus + Dana R2 review.
**Hard prerequisites:** #156 (production startup -- migrations, seeding, data safety, admin key behavior)
**Soft prerequisites:** #158 (admin key UX research -- informs INSTALL.md phrasing only)
**Related cards:** #104 (future user-configuration / hot-reload surface), #154 (CVE response), #155 (README restructure), #157 (dry-run workflow follow-up), #159 (settings resolution architecture, locked)
**Date:** 2026-04-18 (R2 revision)

---

## 1. Overview

This spec defines the v1 release pipeline for Collabhost: the CI workflow that produces platform-specific release archives, the conventions that govern their contents, and the install scripts that turn them into a working installation on an operator's machine.

The shape of the pipeline is: an operator publishes a GitHub Release at tag `v{semver}`, a matrix GitHub Actions workflow fires on `release: [published]`, builds the frontend once and reuses it across five platform legs, downloads a pinned Caddy binary per platform, runs a self-contained single-file `dotnet publish`, packages the result together with Caddy + license files + `INSTALL.md` into a platform-appropriate archive, computes SHA256 checksums, and attaches everything to the Release. Two install scripts (`install.sh`, `install.ps1`) -- hosted via GitHub Pages from `docs/` -- then give operators a one-command experience to download, verify, extract, and PATH-integrate the binary. The result is a single bundle per platform that an operator can unpack and run, with Caddy already present and wired up, and a predictable way to re-run the installer for future updates.

The bar is higher than Collaboard's shipped pipeline in four places: **one** frontend build shared across matrix legs, bundled Caddy (resolved via a locked precedence chain at startup), SHA256 checksums and verification, and macOS Gatekeeper guidance shipped inside the archive. Everything else inherits Collaboard's "manual release trigger, self-contained single-file publish, GitHub Releases as hosting" skeleton.

### 1.1 What changed from R1 (reviewer orientation)

R1 of this spec used "escape hatch" and "fallback chain" vocabulary drawn from infrastructure patterns I'd used elsewhere. Bill's review + the #159 discussion locked a different and cleaner model: **configuration resolves once at startup into a single source of truth**, with a fixed precedence `CLI args > env vars > appsettings.json > hardcoded default`. "Configuration resolution" / "precedence chain" replaces "escape hatch" vocabulary throughout R2. See §2.5.

R1 also proposed a pre-launch probe against `localhost:2019` to detect externally-managed Caddy and silently adopt it ("Probe A"). Marcus's review enumerated the failure modes (foreign-Caddy clobber, concurrent-startup race, false positives). Bill ruled to drop Probe A entirely. The `COLLABHOST_CADDY_PATH` env var covers the 99% case; a future explicit opt-in (e.g., `COLLABHOST_CADDY_ADOPT_EXISTING=true`) can be added if a real user materializes. See §6.4.

R1 gestured at moving `ProxyAppSeeder.SeedAsync` out of `Program.cs`'s `IsDevelopment()` block. Marcus surfaced that EF Core migrations, TypeStore load, and the proxy seeder are **all** inside that gate, and that no backup/safety discipline had been specified for production upgrades. That work is now owned by **card #156** (production startup posture), which is declared a hard prerequisite here. See §6.5, §14.

R1's §12 ("Path / Data Directory Handling") proposed a broad env-var floor using names that did not match the shipped settings surface. #159's settings audit (`.agents/temp/settings-audit-shipped.md`) identified 7 production settings + the admin key special case. R2's §12 reframes the env-var floor against that audit, drops the dead `Platform:ToolsDirectory` key (removed as hygiene on #156), and aligns naming to ASP.NET Core convention where the audit supports it. See §12.

R1 listed six open questions. All six are resolved (§17.2). R1's 5-phase implementation plan is reordered in R2 -- Phase 3 (env-var floor) now precedes Phase 2 (Caddy resolver), per Marcus's argument (Phase 3 is additive and unblocks incremental value), and the Phase 2 production-startup scope move has been carved off to #156. See §18.

---

## 2. Locked Decisions (reference)

These are fixed inputs. The spec designs around them -- it does not re-argue them. Entries updated from R1 are marked `(R2)`.

| # | Decision |
|---|----------|
| 1 | Manual release trigger. Workflow fires on `release: [published]`. Operator creates the Release; the workflow attaches artifacts. |
| 2 | Five platforms: `win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`. |
| 3 | Publish flags: `--self-contained`, `PublishSingleFile=true`. No trim, no ReadyToRun. |
| 4 | Version from Git tag (`v{semver}`, `v` stripped). Exposed at runtime via `GET /api/v1/version` and `--version` CLI flag. Both strip commit-hash suffix. `--version` stdout format is `Collabhost {version}` (see §8.5). **(R2)** |
| 5 | Versioning baseline `v0.1.0`. No pre-release conventions in v1. |
| 6 | Frontend built once, shared across matrix via artifact upload/download. |
| 7 | Archive format: `.zip` on Windows, `.tar.gz` elsewhere. |
| 8 | Archive filename includes version, **no `v` prefix**: `collabhost-{version}-{rid}.{ext}` (e.g., `collabhost-0.1.0-linux-x64.tar.gz`). **(R2)** |
| 9 | Archive contents: Collabhost binary (renamed `collabhost[.exe]`, no longer `Collabhost.Api`), Caddy binary, `appsettings.json`, `INSTALL.md`, `LICENSES/caddy-LICENSE`, `LICENSES/caddy-NOTICE`. **(R2 -- binary rename)** |
| 10 | SHA256 per archive + aggregate `checksums.txt` (standard `sha256sum -c` format). Install scripts verify before extracting. |
| 11 | Caddy bundled. Pinned version. Per-platform download during workflow. Binary resolution at startup is a single-pass precedence chain: (1) `COLLABHOST_CADDY_PATH` env var, (2) `Proxy:BinaryPath` from `appsettings.json` if set, (3) bundled sidecar next to the Collabhost binary. **Probe A (external-Caddy auto-detection on `localhost:2019`) is dropped** per Bill's ruling. Post-launch admin-API probe with 3-5s timeout; on failure, soft-fail with visibility (proxy subsystem disabled this boot, `proxyState` surfaced in `/status` + dashboard). **(R2)** |
| 12 | Install mechanism: Aspire model. `install.sh` + `install.ps1`. Download-then-execute primary. Piped `curl \| bash` / `iwr \| iex` documented as shortcut. Location `$HOME/.collabhost/bin`, user-space. OS/arch detection, SHA256 verification, PATH integration. Merge-safe -- preserves `data/`. `dotnet tool install` and `winget` noted as v1+ follow-ups, NOT built here. **(R2 -- `appsettings.Local.json` removed from the preservation list: production deployments use a single `appsettings.json`, not `.Local`; see §12.)** |
| 13 | Install scripts hosted via GitHub Pages, source `main:/docs`. Public URL `https://mrbildo.github.io/collabhost/install.sh`. Future `docs/CNAME` for `collabhost.dev` noted but not day-1. |
| 14 | Data path default `./data/` relative to binary. Every operator-configurable path in the shipped settings surface exposes a `COLLABHOST_*` env var override. The full resolution model is in §2.5. **(R2 -- reframed: "env-var floor" → "precedence chain, one source of truth at startup".)** |
| 15 | Admin key behavior is owned by **card #156** (production startup). Three scenarios (blind first run, configured first run, override on subsequent boot) are specified there. INSTALL.md section is owned here; the *UX surface* (where stdout output lands, recovery if missed, capture format) is owned by **#158**. This spec references both rather than re-specifying. **(R2 -- was "log to stdout per #152, override via `Admin:AuthKey`"; superseded.)** |
| 16 | macOS Gatekeeper `xattr -d com.apple.quarantine` workaround documented day-1 in INSTALL.md inside the archive. `install.sh` auto-clears on macOS and prints a line confirming it did so. **(R2 -- auto-clear confirmed by Bill.)** |
| 17 | Release hosting: GitHub Releases. No CDN. No external hosting. |
| 18 | NOT in v1: Docker, MSI, .deb/.rpm, Homebrew tap, code signing, notarization. |

### 2.5 Configuration resolution model (locked on #159)

This replaces R1's "escape hatch" framing everywhere it appeared. The #159 outcome is summarized in full on the card; the parts this spec needs to align with are quoted here.

**Mental model.** Configuration resolves **once at startup** into a single complete set of values. It is *not* a runtime fallback chain where each reader walks a hierarchy. Every consumer (Supervisor, Proxy, DataAccess, Auth) reads from the resolved values -- not from a layered source.

**Precedence (locked).** For every resolvable setting:

```
CLI args   >   env vars   >   appsettings.json   >   hardcoded default
```

CLI args are future (not introduced in v1). Env vars and `appsettings.json` are present today. The hardcoded default is whatever the type / registration code specifies when neither upstream source provides a value.

**Scope of this resolution system.** Only **load-time settings** -- the ones that configure the process at startup and do not change without a restart. The audit catalogued 7 such settings + the admin key special case. See §12.

**Runtime-mutable settings are out of scope.** User preferences, feature flags, and hot-reload configuration (if they ever exist) live in a different bag -- DB-backed, outside the startup resolution chain. That bag is **card #104**'s surface, not this spec's.

**The admin key is a documented exception to "env var → resolved value."** It still resolves once at startup, but its destination is the SQLite DB (seed and/or override) rather than the in-memory configuration. Timing is the same (single-pass, startup). Behavior is owned by #156.

**`appsettings.json` vs `appsettings.Local.json` vs `appsettings.Production.json`.** Production deployments use **a single `appsettings.json`** -- the one shipped in the archive, which operators may edit in place. `.Local.json` is a dev-only convention (already used for local overrides during development). There is no `appsettings.Production.json`. Operators who want to keep their changes separate from the shipped file can set env vars instead.

**Vocabulary for the rest of the spec.** "Configuration resolution" = the whole single-pass mechanism. "Precedence chain" = the `CLI > env > config > default` ordering. "Env-var override" = the intermediate source in the chain. The term "escape hatch" from R1 is retired.

---

## 3. Workflow Design

### 3.1 File location and event

A new workflow file at `.github/workflows/publish.yml`, sibling to the existing `ci.yml`. Trigger:

```yaml
name: Publish

on:
  release:
    types: [published]
```

No `workflow_dispatch`, no `push: tags:`, no PR trigger. The only path into this workflow is an operator (or `gh release create ... --generate-notes`) publishing a GitHub Release. This matches decision 1 and Collaboard's model verbatim.

### 3.2 Permissions and secrets

```yaml
permissions:
  contents: write   # needed for gh release upload
```

No other permissions. No external secrets. `${{ github.token }}` is sufficient for `gh release upload`. This is also the Collaboard model.

### 3.3 Job graph

Four jobs, connected by `needs:` + `upload-artifact` / `download-artifact`:

```
        extract-version
              │
              ▼
         build-frontend ─────┐
              │              │
              ▼              ▼
         build-matrix (5 legs, each depends on build-frontend + extract-version)
              │
              ▼
       publish-checksums (aggregates per-leg checksums into checksums.txt and uploads)
```

**Rationale for splitting `extract-version`:** Decision 4 requires that `v{semver}` be stripped to `{semver}` and injected via `/p:Version=`. Putting this in its own job makes the stripped version a named output consumed by `build-frontend` (for display / cache-bust metadata if we want it) and every leg of `build-matrix`. It also means a bad tag format fails fast before any dotnet/node work runs.

**Rationale for `build-frontend` as its own job:** Decision 6 says build once. A separate job uploads a `frontend-dist` artifact. Each matrix leg downloads it, drops it into `backend/Collabhost.Api/wwwroot/`, and then runs `dotnet publish`. Five `vite build` invocations collapse into one. On Collaboard this cost ~5x; for Collabhost it is the correct defaulted pattern.

**Rationale for `publish-checksums` last:** Each matrix leg emits a per-archive `<archive>.sha256` file and uploads it. The trailing job downloads all of them, concatenates into `checksums.txt`, and uploads that one aggregated file to the Release. This keeps the per-leg steps simple and gives operators one file to point `sha256sum -c` at.

### 3.4 `extract-version` job

```yaml
extract-version:
  runs-on: ubuntu-latest
  outputs:
    version: ${{ steps.version.outputs.VERSION }}
  steps:
    - name: Parse tag
      id: version
      run: |
        TAG="${{ github.event.release.tag_name }}"
        if [[ ! "$TAG" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
          echo "Tag '$TAG' does not match vX.Y.Z (v1 does not support pre-releases)" >&2
          exit 1
        fi
        echo "VERSION=${TAG#v}" >> "$GITHUB_OUTPUT"
```

The regex enforces decision 5 (no pre-releases in v1). A later version can relax this to `^v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?$`. Failing early here means we never ship a half-tagged release.

### 3.5 `build-frontend` job

```yaml
build-frontend:
  needs: extract-version
  runs-on: ubuntu-latest
  defaults: { run: { working-directory: frontend } }
  steps:
    - uses: actions/checkout@v4
    - uses: actions/setup-node@v4
      with:
        node-version: '22'
        cache: npm
        cache-dependency-path: frontend/package-lock.json
    - run: npm ci
    - run: npx vite build
    - uses: actions/upload-artifact@v4
      with:
        name: frontend-dist
        path: frontend/dist
        retention-days: 1
```

**Retention:** 1 day. These are transient CI artifacts; the user-facing artifacts are the release archives, which GitHub retains per-release.

### 3.6 `build-matrix` job

```yaml
build-matrix:
  needs: [extract-version, build-frontend]
  runs-on: ${{ matrix.os }}
  strategy:
    fail-fast: false
    matrix:
      include:
        - rid: win-x64
          os: windows-latest
          caddy_asset: caddy_{CADDY_VER}_windows_amd64.zip
          caddy_bin: caddy.exe
          ext: zip
        - rid: osx-arm64
          os: macos-latest
          caddy_asset: caddy_{CADDY_VER}_mac_arm64.tar.gz
          caddy_bin: caddy
          ext: tar.gz
        - rid: osx-x64
          os: macos-latest
          caddy_asset: caddy_{CADDY_VER}_mac_amd64.tar.gz
          caddy_bin: caddy
          ext: tar.gz
        - rid: linux-x64
          os: ubuntu-latest
          caddy_asset: caddy_{CADDY_VER}_linux_amd64.tar.gz
          caddy_bin: caddy
          ext: tar.gz
        - rid: linux-arm64
          os: ubuntu-latest
          caddy_asset: caddy_{CADDY_VER}_linux_arm64.tar.gz
          caddy_bin: caddy
          ext: tar.gz
```

**Choice: `os` per-leg instead of Collaboard's "all on ubuntu-latest".** This is worth calling out -- Collaboard uses `ubuntu-latest` for every leg because cross-RID `dotnet publish` works fine. For v1 we can do the same. The reason to consider native runners is (a) Windows single-file bundler behavior on the actual OS, (b) macOS archive permissions (`tar` on macOS vs Linux), (c) future signing/notarization (deferred, but the matrix structure should not rewrite later). Recommendation: **use native runners (`windows-latest` / `macos-latest` / `ubuntu-latest`)**. The cost is slightly longer CI minutes (~2-3 min overhead per leg); the benefit is that the archive is built on the platform it ships for, and the `tar` / `zip` tooling is native. This is a small departure from Collaboard but is defensible and future-friendly.

**Alternative considered:** all on `ubuntu-latest` with `tar` + `zip` installed, following Collaboard. Works, but then Windows archives are built by Linux `zip`, which is fine today but nudges future signing off a cliff. Ruling: native runners.

Steps inside each matrix leg:

```yaml
    steps:
      - uses: actions/checkout@v4

      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      # 1. Bring the pre-built frontend into the API project
      - uses: actions/download-artifact@v4
        with:
          name: frontend-dist
          path: backend/Collabhost.Api/wwwroot

      # 2. Download pinned Caddy binary (see section 6)
      - name: Download Caddy
        shell: bash
        run: |
          VER=$(cat caddy.version)
          ASSET="${{ matrix.caddy_asset }}"
          ASSET="${ASSET/\{CADDY_VER\}/${VER}}"
          URL="https://github.com/caddyserver/caddy/releases/download/v${VER}/${ASSET}"

          mkdir -p caddy-download
          curl -fsSL --retry 3 --retry-delay 5 -o "caddy-download/${ASSET}" "$URL"

          case "$ASSET" in
            *.zip)    unzip -p "caddy-download/${ASSET}" "${{ matrix.caddy_bin }}" > caddy-download/${{ matrix.caddy_bin }} ;;
            *.tar.gz) tar -xzf "caddy-download/${ASSET}" -C caddy-download "${{ matrix.caddy_bin }}" ;;
          esac
          chmod +x "caddy-download/${{ matrix.caddy_bin }}" || true

      # 3. Publish self-contained single-file
      #    AssemblyName=collabhost renames the published executable from
      #    Collabhost.Api[.exe] to collabhost[.exe]. The internal assembly name
      #    on disk changes; the C# namespace and csproj are unchanged.
      - name: Publish
        working-directory: backend
        run: >
          dotnet publish Collabhost.Api/Collabhost.Api.csproj
          -c Release
          -r ${{ matrix.rid }}
          --self-contained
          -p:PublishSingleFile=true
          -p:AssemblyName=collabhost
          -p:Version=${{ needs.extract-version.outputs.version }}
          -o ../publish/collabhost-${{ needs.extract-version.outputs.version }}-${{ matrix.rid }}

      # 4. Stage archive contents
      - name: Stage archive
        shell: bash
        env:
          VER: ${{ needs.extract-version.outputs.version }}
        run: |
          STAGE="publish/collabhost-${VER}-${{ matrix.rid }}"
          mkdir -p "${STAGE}/LICENSES"
          cp caddy-download/${{ matrix.caddy_bin }} "${STAGE}/"
          cp release-assets/INSTALL.md "${STAGE}/"
          cp release-assets/caddy-LICENSE "${STAGE}/LICENSES/"
          cp release-assets/caddy-NOTICE  "${STAGE}/LICENSES/"
          # appsettings.json is already emitted by dotnet publish

      # 5. Archive
      - name: Archive (zip)
        if: matrix.ext == 'zip'
        shell: pwsh
        env:
          VER: ${{ needs.extract-version.outputs.version }}
        run: |
          Compress-Archive -Path "publish/collabhost-${env:VER}-${{ matrix.rid }}/*" -DestinationPath "collabhost-${env:VER}-${{ matrix.rid }}.zip"

      - name: Archive (tar.gz)
        if: matrix.ext == 'tar.gz'
        shell: bash
        env:
          VER: ${{ needs.extract-version.outputs.version }}
        run: |
          tar -czf "collabhost-${VER}-${{ matrix.rid }}.tar.gz" -C "publish/collabhost-${VER}-${{ matrix.rid }}" .

      # 6. Per-archive checksum
      - name: Checksum
        shell: bash
        env:
          VER: ${{ needs.extract-version.outputs.version }}
        run: |
          ARCHIVE="collabhost-${VER}-${{ matrix.rid }}.${{ matrix.ext }}"
          if command -v sha256sum >/dev/null 2>&1; then
            sha256sum "$ARCHIVE" > "$ARCHIVE.sha256"
          else
            shasum -a 256 "$ARCHIVE" > "$ARCHIVE.sha256"
          fi

      # 7. Upload archive + checksum to the release
      - name: Upload release assets
        env:
          GH_TOKEN: ${{ github.token }}
          VER: ${{ needs.extract-version.outputs.version }}
        shell: bash
        run: |
          gh release upload "${{ github.event.release.tag_name }}" \
            "collabhost-${VER}-${{ matrix.rid }}.${{ matrix.ext }}" \
            "collabhost-${VER}-${{ matrix.rid }}.${{ matrix.ext }}.sha256" \
            --clobber

      # 8. Upload per-leg checksum to workflow artifacts for aggregation
      - uses: actions/upload-artifact@v4
        with:
          name: checksum-${{ matrix.rid }}
          path: collabhost-*-${{ matrix.rid }}.${{ matrix.ext }}.sha256
          retention-days: 1
```

Notes on the leg:

- `release-assets/` is a new folder in the repo (see section 5.3 and 11) holding `INSTALL.md`, `caddy-LICENSE`, `caddy-NOTICE`. These are sources; the workflow copies them into the staged archive.
- `caddy.version` at the repo root is the version pin (see section 6.1). Single source of truth.
- `--clobber` on `gh release upload` lets us re-run the workflow against the same release without manual cleanup.
- Individual `<archive>.sha256` files are uploaded to the release directly so manual downloaders can verify without pulling the aggregated file -- but install scripts use the aggregated `checksums.txt` (section 7).

### 3.7 `publish-checksums` job

```yaml
publish-checksums:
  needs: build-matrix
  runs-on: ubuntu-latest
  steps:
    - uses: actions/download-artifact@v4
      with:
        pattern: checksum-*
        merge-multiple: true
        path: checksums-in
    - name: Aggregate
      run: cat checksums-in/*.sha256 > checksums.txt
    - name: Upload to release
      env:
        GH_TOKEN: ${{ github.token }}
      run: gh release upload "${{ github.event.release.tag_name }}" checksums.txt --clobber
```

`cat *.sha256 > checksums.txt` produces a standard `sha256sum -c` file: one line per archive, format `<64-char-hex>  <filename>`. Install scripts grep this file for their target archive (section 7 and 9).

### 3.8 Workflow-level observations

- No retry on the matrix itself. The Caddy download step has `--retry 3 --retry-delay 5` because GitHub Releases has occasional transient 5xx. A failed matrix leg surfaces clearly and can be re-run via the GitHub UI; `--clobber` on the uploads keeps this idempotent.
- `fail-fast: false` so one bad leg does not cancel the others. Operators can see exactly which platform failed and re-run only that leg.
- No caching of NuGet packages in `build-matrix`. The five legs run in parallel anyway; per-leg NuGet restore is cheap enough and avoids cache-key complications across RIDs.

---

## 4. Build Matrix

Canonical RID list:

| RID | Archive (at v0.1.0) | Caddy asset | Notes |
|-----|---------|-------------|-------|
| `win-x64` | `collabhost-0.1.0-win-x64.zip` | `caddy_{VER}_windows_amd64.zip` | `collabhost.exe`, `caddy.exe` |
| `osx-arm64` | `collabhost-0.1.0-osx-arm64.tar.gz` | `caddy_{VER}_mac_arm64.tar.gz` | Apple Silicon; primary Mac target |
| `osx-x64` | `collabhost-0.1.0-osx-x64.tar.gz` | `caddy_{VER}_mac_amd64.tar.gz` | Intel Mac |
| `linux-x64` | `collabhost-0.1.0-linux-x64.tar.gz` | `caddy_{VER}_linux_amd64.tar.gz` | |
| `linux-arm64` | `collabhost-0.1.0-linux-arm64.tar.gz` | `caddy_{VER}_linux_arm64.tar.gz` | Raspberry Pi 4/5, ARM servers |

Archive naming convention (fixed, R2): `collabhost-{version}-{rid}.{ext}`. No `v` prefix on the version in the filename -- the Git tag carries `v`, the filename carries the stripped semver. This matches Dana's concern from R1 review: out-of-band distribution (downloads folder, bug reports, email attachments) loses all version context if the filename omits it; the install script "complexity" of one extra variable substitution is trivial.

**Binary rename (R2).** The shipped binary is `collabhost[.exe]`, not `Collabhost.Api[.exe]`. The internal assembly name remains `Collabhost.Api` (namespace-stable), but the published output is renamed. Operators type `collabhost --version`, not `Collabhost.Api --version`. See §3.6 for the `AssemblyName` / `-p:AssemblyName=collabhost` override in the publish step, and §8.5 for how this lands in PATH.

Sha256 asset names mirror the archive: `collabhost-{version}-{rid}.{ext}.sha256`.

---

## 5. Artifact Layout

### 5.1 Archive tree (platform-agnostic)

```
collabhost-0.1.0-<rid>/
├── collabhost              (or collabhost.exe on win-x64)   [~80 MB, self-contained single-file]
├── caddy                   (or caddy.exe on win-x64)        [~40 MB, pinned upstream build]
├── appsettings.json                                         [~500 bytes, shipped default config]
├── INSTALL.md                                               [~3-4 KB, post-Dana expansion]
└── LICENSES/
    ├── caddy-LICENSE                                        [~11 KB, Apache 2.0 full text]
    └── caddy-NOTICE                                         [~100 bytes]
```

### 5.2 What each file is

- **`collabhost[.exe]`** -- the API + embedded SPA + native SQLite + .NET runtime. Self-contained single-file. No external runtime requirement. Renamed from `Collabhost.Api[.exe]` per R2 decision 9 via `-p:AssemblyName=collabhost` at publish time.
- **`caddy[.exe]`** -- the pinned Caddy binary. Upstream build from github.com/caddyserver/caddy releases. Supervised as a child process by Collabhost under the `proxy` system-service app.
- **`appsettings.json`** -- the shipped default configuration. Contains the data-path default, proxy defaults, typestore defaults. Operators edit this file in place, or set env vars per §12; there is no `appsettings.Production.json` and no `.Local.json` in the production model (see §2.5).
- **`INSTALL.md`** -- post-install operator guide. Ships inside the archive so manual downloaders get it without leaving the extracted directory. Dana's R1 review expanded the section list; see §13.
- **`LICENSES/caddy-LICENSE`** -- full Apache 2.0 text (required by §4 of the license when redistributing).
- **`LICENSES/caddy-NOTICE`** -- the literal contents of Caddy's NOTICE file:
  ```
  Caddy
  Copyright 2015 Matthew Holt and The Caddy Authors
  ```

Collabhost itself is MIT (per `Directory.Build.props`) and does not require a separate NOTICE file. The repo root `LICENSE` governs Collabhost's own source distribution; it is intentionally **not** duplicated into the archive -- the archive is a binary distribution of a binary plus third-party attributions. We can add a `LICENSES/collabhost-LICENSE` later if an operator asks.

### 5.3 Size estimates (ballpark)

| Component | Approx uncompressed | Approx compressed |
|-----------|---------------------|-------------------|
| `collabhost[.exe]` (self-contained single-file, no trim, no R2R) | 75-95 MB | 30-40 MB |
| `caddy[.exe]` | 40-45 MB | 18-22 MB |
| `appsettings.json` + `INSTALL.md` + `LICENSES/*` | ~15 KB | ~5 KB |
| **Total archive (.tar.gz)** | ~120-140 MB | **~50-65 MB** |
| **Total archive (.zip, win-x64)** | ~120-140 MB | **~55-70 MB** |

The .NET self-contained + single-file + native-libs-for-self-extract footprint is the dominant cost. `PublishTrimmed` would cut ~30-40%, but we're leaving it off for v1 to avoid runtime surprises on reflection-heavy code (EF Core, MCP SDK, JSON source generators). Revisit in a follow-up card once we have real operator telemetry that archive size matters.

### 5.4 What's explicitly NOT in the archive

- `appsettings.Local.json` / `appsettings.Development.json` -- dev-only overlays, not copied into production artifacts. The production model uses a single `appsettings.json` (see §2.5).
- `appsettings.Production.json` -- does not exist as a convention in Collabhost. Operators edit `appsettings.json` or set env vars.
- `data/` -- created at first run by the Collabhost binary. Never shipped.
- The repo `README.md` -- not shipped in archive; it's the web-readable repo landing page (post-#155).
- Source code -- available via the GitHub repo.
- Caddy source -- Apache 2.0 does not require source distribution. We distribute the upstream binary + LICENSE + NOTICE, which is compliant.
- `install.sh` / `install.ps1` -- these live in `docs/` for GitHub Pages hosting. They are not inside the archive because the archive is what the install script produces.

---

## 6. Caddy Bundling Details

### 6.1 Version pin location

**Recommendation: a plain-text file `caddy.version` at the repo root.**

Contents (example):
```
2.11.2
```

**Why this and not MSBuild property / `deps.json` / env var in the workflow:**

- The workflow reads it in a single step (`VER=$(cat caddy.version)`). Simple.
- The install scripts and docs can reference it for operator messaging without touching MSBuild.
- It's a one-liner to bump. A PR that changes Caddy is a single-file diff -- trivial to review.
- It lives next to `.editorconfig`, `nuget.config`, `aspire.config.json`, etc. Repo-root config files are where we keep other cross-cutting pins.
- It's one step away from a future `deps.json` if we end up with more bundled third-party binaries (Node for nodejs-app type, etc.). When that happens, we promote to a structured manifest; for now, flat file wins.

**Recommended pin for first release: Caddy `v2.11.2`.** The research surfaced Caddy's CVE cadence has been active in 2025-2026 -- 2.10.0 fixed a cluster, a 2026 batch is recent. Pin to the latest stable release as of the first Collabhost release tag. Bill should confirm at implementation time which stable is current, but v2.11.x is the current line.

**CVE response (scope boundary):** Card #154 owns the process for responding to Caddy CVEs. This spec only defines where the pin lives and how it threads through the workflow. Bumping the pin is a one-line change here; the release cadence / triage is #154's problem.

### 6.2 How the workflow downloads Caddy per platform

Single step per matrix leg (already shown in section 3.6). Key properties:

- **URL template:** `https://github.com/caddyserver/caddy/releases/download/v${VER}/${ASSET}`. Stable, public, HTTPS, no auth needed.
- **Asset naming:** Caddy's release assets follow the pattern `caddy_{version}_{os}_{arch}.{ext}`. The matrix row carries the right filename pattern; the workflow substitutes `{CADDY_VER}` and then fetches.
- **Retry:** `curl --retry 3 --retry-delay 5` for transient GitHub 5xx.
- **No checksum verification on the Caddy download** (in v1). Caddy publishes checksums in a sibling file; we could add verification, and should. Left as a listed risk in section 17 with a trivial mitigation ready.
- **Extraction:** `unzip -p` on Windows (`.zip`), `tar -xzf` elsewhere. Only the `caddy`/`caddy.exe` binary is extracted; Caddy's archive also contains `LICENSE`, `README.md`, etc., which we do not need (we ship our own `caddy-LICENSE` / `caddy-NOTICE`).
- **Permission bit:** `chmod +x caddy-download/caddy || true`. `true` because Windows doesn't need it.

### 6.3 License file handling

`release-assets/caddy-LICENSE` and `release-assets/caddy-NOTICE` live in the repo (under a new `release-assets/` folder). Both are reviewed at the time we pin a new Caddy version -- they're stable, but Apache 2.0's §4 requires that we ship whatever NOTICE the upstream version ships. Implementation should include a task in the "update Caddy version" runbook: diff `caddy-LICENSE` and `caddy-NOTICE` against the new upstream when bumping `caddy.version`.

Exact NOTICE contents to ship (verbatim from upstream):
```
Caddy
Copyright 2015 Matthew Holt and The Caddy Authors
```

Exact LICENSE contents: the full Apache 2.0 text (not reproduced here; copy from `github.com/caddyserver/caddy/blob/master/LICENSE` at pin time).

### 6.4 Caddy binary resolution and post-launch probe

**Context check (codebase state, April 2026).** The current `ProxySettings.BinaryPath` defaults to `"caddy"` (bare name, resolved via PATH using a `where`/`which` subprocess in `ProxyAppSeeder.ResolveFromPath`). The current logic is single-path: if that fails, the proxy app is not seeded and the warning in `ProxyAppSeeder.SeedAsync` tells the user to install Caddy or set a custom path.

For v1 release, this resolution is reshaped into a **single-pass precedence chain** aligned with §2.5's resolution model. Two separate mechanisms:

1. **Binary resolution** -- which Caddy binary to launch. See 6.4.1.
2. **Post-launch health probe** -- after launch, verify Caddy is responding on its admin port. See 6.4.2.

**Probe A is dropped.** R1 proposed a pre-launch probe that detected an existing Caddy on `localhost:2019` and silently adopted it. Marcus's review enumerated three failure modes where the probe returns 2xx but the semantics are wrong (foreign-Caddy clobber, two-instance startup race, false positive on an unrelated service). Bill ruled to drop Probe A entirely. If a future operator wants Collabhost to use an externally-managed Caddy, the path is an explicit opt-in env var (e.g., `COLLABHOST_CADDY_ADOPT_EXISTING=true`) -- deferred until a real user materializes.

#### 6.4.1 Caddy binary precedence chain

Resolution is a pure function, called once during `Proxy/_Registration.cs`. The precedence mirrors §2.5's general rule (`env > config > default`) applied to the Caddy binary specifically:

```
function ResolveCaddyBinary(proxySettings):
    // 1. COLLABHOST_CADDY_PATH env var (highest precedence among operator-visible sources)
    envOverride = Environment.GetEnvironmentVariable("COLLABHOST_CADDY_PATH")
    if envOverride is non-empty:
        if File.Exists(envOverride):
            return envOverride (absolute)
        log warning: "COLLABHOST_CADDY_PATH set to '{envOverride}' but file not found; falling through to config / bundled"

    // 2. Proxy:BinaryPath from appsettings.json, when set to a non-empty value
    //    Absolute path -> used as-is. Bare name -> PATH resolution (existing ResolveFromPath behavior).
    if proxySettings.BinaryPath is non-empty:
        resolved = ResolveBinaryPathSetting(proxySettings.BinaryPath)  // existing where/which logic for bare names
        if resolved is non-empty and File.Exists(resolved):
            return resolved
        log warning: "Proxy:BinaryPath='{proxySettings.BinaryPath}' could not be resolved; falling through to bundled"

    // 3. Bundled sidecar next to the Collabhost binary (the v1 default shipped in the archive)
    bundledPath = Path.Combine(AppContext.BaseDirectory,
                               OperatingSystem.IsWindows() ? "caddy.exe" : "caddy")
    if File.Exists(bundledPath):
        return bundledPath

    return null  // no Caddy available; proxy subsystem disabled (see 6.4.2 soft-fail)
```

**Ordering rationale (locked by Bill):**
- **Env var first.** Highest-precedence operator-visible source. Aligns with §2.5's general precedence chain.
- **`Proxy:BinaryPath` second.** The operator's explicit choice in `appsettings.json`. Aligns with ASP.NET Core's standard config convention: env var overrides config, config overrides default.
- **Bundled sidecar third (the default).** This is the shipped artifact; it's what operators get unless they've deliberately chosen otherwise. Correctness-wise, bundling is the floor: without it, a fresh install with no env var and no config edit would have nowhere to look.

**Difference from R1.** R1 placed bundled second and legacy-config last. Marcus argued (and Bill agreed) that the correct order for *operator-specified* paths is `env > config > default`. That's what ASP.NET Core itself does, and it's what the resolution model §2.5 locked in. "Bundled" is the hardcoded default for the Caddy binary -- same bucket as any other hardcoded default in the precedence chain.

**What happens to the existing `"Proxy:BinaryPath": "caddy"` default in `appsettings.json`?** Change the default to empty string (or omit the key entirely, leaving it unbound). The bundled sidecar fallback becomes the production default. Dev-time developers who rely on PATH-resolved Caddy set `Proxy:BinaryPath = "caddy"` in `appsettings.Local.json` (which only exists in dev, per §2.5). This is a contained behavior change with a release-note line but no user-visible rupture.

#### 6.4.2 Post-launch admin-API probe (soft-fail with visibility)

Decision 11 requires that after launching Caddy, we probe the admin API with a 3-5s timeout. The current `CaddyClient.IsReadyAsync` method exists and is used elsewhere; we wrap it in a retry loop that gives early-success short-circuit *and* handles slow-start variance:

```
async function VerifyCaddyReady(caddyClient, logger) -> bool:
    deadline = Now + 5 seconds
    perAttemptTimeout = 1 second      // Marcus's concern from R1: avoid a single 5s hang
    while Now < deadline:
        try:
            if await caddyClient.IsReadyAsync(perAttemptTimeout):
                return true
        catch TimeoutException or HttpRequestException:
            // expected during warm-up; continue polling
        await Delay(200ms)
    return false
```

**Per-attempt timeout of 1s** addresses Marcus's R1 concern: a single 5s `IsReadyAsync` call that hangs the full budget leaves zero retries. Poll every ~1s with shorter per-attempt timeouts so slow-but-alive Caddy startup (ARM cold boot, SSD under load) still recovers.

**On success:** proxy subsystem runs as normal.

**On failure (soft-fail with visibility):** Bill's locked ruling is soft-fail plus loud visibility -- not silent degradation, not API process kill.

1. Log at `LogLevel.Error` with a clear remediation message pointing at `COLLABHOST_CADDY_PATH` and the bundled binary's expected location.
2. Disable the proxy subsystem for this process lifetime. `ProxyManager` stops attempting to sync routes; no further Caddy admin API calls happen.
3. **Surface the state externally.** Extend `GET /api/v1/status` to include a `proxyState` field with values:
   - `"running"` -- Caddy is up, admin API reachable, route sync is active.
   - `"disabled"` -- no Caddy binary was resolved (ResolveCaddyBinary returned null).
   - `"failed"` -- Caddy was launched but the admin-API probe failed within the 5s budget. The most operationally actionable state.
   - `"stopped"` -- proxy app is explicitly not running (operator stopped it via the UI / API).
   - (Future, if COLLABHOST_CADDY_ADOPT_EXISTING lands: `"external"`.)
4. The dashboard's existing `StatusStrip` consumes `proxyState` and shows a visible-but-non-blocking indicator. Dana owns the UI affordance; this spec names the contract.

The difference from R1: R1 framed this as "soft-fail and log fatal." Marcus's pushback was that logs aren't visibility -- operators never tail logs during routine runs, and the dashboard currently has no way to say "proxy is dead." `/status`'s `proxyState` field is the visibility contract. This is what makes soft-fail safe.

**What the API does without proxy.** The registry, supervisor, dashboard, MCP endpoints, `/status`, logs, and all managed-app process operations continue to function. What doesn't work: HTTPS routing to `{slug}.collab.internal` domains. Operators diagnose Caddy, restart Collabhost, and are back online.

**This is a behavior change from silent startup-ignore-on-failure (today) to noisy soft-fail-with-visibility (v1).** Release notes must call it out.

### 6.5 Affected source files

Concrete list of files this introduces or changes in scope-of-#153. The `IsDevelopment()` gate lifts in `Program.cs` (for migrations, TypeStore load, and `ProxyAppSeeder.SeedAsync`) are **not in #153's scope** -- they belong to **card #156** (production startup posture), a hard prerequisite.

| File | Change |
|------|--------|
| `backend/Collabhost.Api/appsettings.json` | Change `Proxy:BinaryPath` default from `"caddy"` to `""` (or remove the key). Document in release notes. |
| `backend/Collabhost.Api/Proxy/ProxySettings.cs` | Relax `BinaryPath` to non-required (nullable / default empty). Today the `required` keyword throws if the section is missing -- that gate has to come off. |
| `backend/Collabhost.Api/Proxy/ProxyAppSeeder.cs` | `ResolveBinaryPath` delegates to the new `CaddyResolver`. The warning-log path when no Caddy is found stays, but its message is updated to reference `COLLABHOST_CADDY_PATH` and the bundled fallback. |
| `backend/Collabhost.Api/Proxy/CaddyResolver.cs` (NEW) | The precedence chain from §6.4.1 as a pure static function. No I/O outside `File.Exists` and env-var reads. No probe against `localhost:2019` (Probe A is dropped). |
| `backend/Collabhost.Api/Proxy/_Registration.cs` | Wire `CaddyResolver` as the binary resolver for `ProxyAppSeeder`. No changes to `HttpClient<CaddyClient>` setup -- the admin port is still OS-allocated via `PortAllocator` and wired through the bootstrap config. |
| `backend/Collabhost.Api/Proxy/ProxyManager.cs` | On startup, after Supervisor promotes Caddy to `Running`, await `VerifyCaddyReady` (§6.4.2). On success, proceed to route sync. On failure, set internal proxy state to `"failed"`, emit an error log with remediation text, and stop attempting sync for this process lifetime. Existing 2-second startup-delay code stays for now; can be revisited. |
| `backend/Collabhost.Api/System/SystemEndpoints.cs` | Add `proxyState` to the `GET /api/v1/status` response (§6.4.2). Values: `"running" \| "disabled" \| "failed" \| "stopped"`. Source: `ProxyManager` exposes a `CurrentState` property (new) that `SystemEndpoints` reads. |
| `backend/Collabhost.Api/Proxy/ProxyManager.cs` (continuation) | Publicly expose `CurrentState` so `SystemEndpoints` and any dashboard-side consumers can read it. |
| `backend/Collabhost.Api/Proxy/CaddyClient.cs` | Narrow the blanket `catch` at line ~32 to `HttpRequestException` / `TaskCanceledException` / `OperationCanceledException`. Marcus's O5 finding from R1 -- load-bearing for the new probe. |

**Test files that will need updates:** `ProxyAppSeederTests`, `ProxyArgumentProviderTests`, anything that currently assumes a specific `BinaryPath`. List in §15.

**Cross-team coordination:** Dana's frontend work to render `proxyState` in the dashboard `StatusStrip` is in scope of #155 / frontend follow-up. Not in this backend-only card, but named here so the seam is visible.

---

## 7. Checksum Generation

### 7.1 Per-archive format

Each matrix leg produces `collabhost-<version>-<rid>.<ext>.sha256` with one line:

```
<64-char-hex-sha256>  collabhost-<version>-<rid>.<ext>
```

This is the exact output of `sha256sum <archive>` (Linux) or `shasum -a 256 <archive>` (macOS). The two-space separator and trailing newline are the portable-standard format understood by `sha256sum -c` and `shasum -a 256 -c`.

Each file is uploaded to the Release alongside its archive (step 7 of the matrix job). Manual downloaders can verify with:

```bash
# Download both files, then:
sha256sum -c collabhost-0.1.0-linux-x64.tar.gz.sha256
```

### 7.2 Aggregate `checksums.txt`

The `publish-checksums` job concatenates all per-leg sha256 files into one `checksums.txt`:

```
a1b2c3...  collabhost-0.1.0-linux-x64.tar.gz
d4e5f6...  collabhost-0.1.0-linux-arm64.tar.gz
...
```

This file is uploaded to the Release and is the one install scripts reference. `sha256sum -c checksums.txt` verifies all five archives in one command.

### 7.3 How install scripts verify

See section 9.5. Essentially: download `checksums.txt`, grep the line for the target archive, compute sha256 of the downloaded archive locally, compare. Abort if mismatch. This matches the community-standard pattern and requires no new tooling on the operator's machine (both `sha256sum` and PowerShell's `Get-FileHash` are always available).

---

## 8. Version Injection

### 8.1 Tag parsing

Already designed in section 3.4. `v{semver}` -> `{semver}`. The workflow fails with a clear message for pre-release tags.

### 8.2 Build-time injection

```
/p:Version=${{ needs.extract-version.outputs.version }}
```

.NET rolls `Version` into `AssemblyVersion`, `AssemblyFileVersion`, and `AssemblyInformationalVersion` automatically. The Informational version is the one we read at runtime because it preserves full semver (e.g., `0.1.0`, not `0.1.0.0`).

### 8.3 Runtime version helper (consolidation)

**Recommendation: one static helper, `Collabhost.Api.Platform.VersionInfo`.** Today the assembly-attribute read is duplicated in three places (`SystemEndpoints.cs:22`, `DiscoveryTools.cs:61`, `_McpRegistration.cs:14`) and no place strips the commit-hash suffix. Centralize it:

```
// backend/Collabhost.Api/Platform/VersionInfo.cs
namespace Collabhost.Api.Platform;

public static class VersionInfo
{
    private static readonly Lazy<string> _version = new(Compute);

    public static string Current => _version.Value;

    private static string Compute()
    {
        var raw = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "0.0.0";

        // Strip "+<commit-hash>" suffix if the SDK added one
        var plusIndex = raw.IndexOf('+', StringComparison.Ordinal);
        return plusIndex >= 0 ? raw[..plusIndex] : raw;
    }
}
```

Update three call sites to use `VersionInfo.Current`. This also fixes the drift across tools: today each caller could diverge in stripping behavior; after this, they can't.

### 8.4 `/api/v1/version` endpoint contract

New endpoint in `SystemEndpoints`:

```
GET /api/v1/version

Response 200:
{
  "version": "0.1.0"
}
```

- Public, no auth required (like `/api/v1/status`).
- Returns `VersionInfo.Current`.
- No extra fields in v1 -- resist the urge to stuff in commit hash, build date, etc. If operators ask later, add as a `detail` sub-object; don't change the shape of `version`.

The existing `/api/v1/status` endpoint already returns `version` as part of the `SystemStatus` record. Keep that -- it's useful for the dashboard. The new `/api/v1/version` is a cleaner single-purpose endpoint for scripting ("what version is running?").

### 8.5 `--version` CLI flag contract

Behavior: if `args` contains `--version` or `-v`, print the version to stdout and exit 0 *before* building the web host.

**Locked format (R2): `Collabhost {version}\n`** -- e.g., `Collabhost 0.1.0\n`.

```csharp
// At top of Program.cs, before WebApplication.CreateBuilder:
if (args.Any(a => a is "--version" or "-v"))
{
    Console.WriteLine($"Collabhost {Collabhost.Api.Platform.VersionInfo.Current}");
    return 0;
}
```

**Rationale (Dana's R1 argument, accepted by Bill).** The primary consumer is a human operator pasting into a bug report, support ticket, or terminal history. A bare `0.1.0` disconnects from the identity of the thing emitting it ("version of what?"). `Collabhost 0.1.0` is self-documenting. Scripts that need only the number can `cut -d' ' -f2` or regex -- the friction cost on automation is smaller than the friction cost on human identification. Machine consumers with network access use `GET /api/v1/version`, which returns a structured `{"version": "..."}` object.

Exit code: 0.

Edge cases:
- `--version` before any other argument works because we short-circuit at the top of Program.cs.
- Combining `--version` with other flags: we exit immediately, ignoring them. This is the POSIX convention.
- `-v` could collide with a verbose flag later. Keep `--version` as the canonical; `-v` is a convenience alias we can drop if it conflicts.
- CI self-check (§15.3) must grep for the version *number*, not assume bare output -- updated example in §15.3.

### 8.6 Where the version-strip logic lives

Centralized in `VersionInfo.Compute`. One place. Don't inline the `+` split in `SystemEndpoints` or anywhere else. The three existing assembly-attribute reads move to this helper.

---

## 9. Install Scripts

### 9.1 File locations

- `docs/install.sh` -- POSIX sh (bash-compatible). Linux + macOS.
- `docs/install.ps1` -- PowerShell (Windows Powershell 5+ / pwsh 7+).

These live in `docs/` for GitHub Pages hosting (section 10). They are checked into the repo.

### 9.2 Detected arch -> RID mapping

**`install.sh`:**

```bash
case "$(uname -s)-$(uname -m)" in
  Linux-x86_64)    RID=linux-x64   ; EXT=tar.gz ;;
  Linux-aarch64)   RID=linux-arm64 ; EXT=tar.gz ;;
  Linux-arm64)     RID=linux-arm64 ; EXT=tar.gz ;;
  Darwin-x86_64)   RID=osx-x64     ; EXT=tar.gz ;;
  Darwin-arm64)    RID=osx-arm64   ; EXT=tar.gz ;;
  *) echo "Unsupported platform: $(uname -s)-$(uname -m)" >&2; exit 1 ;;
esac
```

**`install.ps1`:**

```powershell
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($arch -eq 'X64')    { $Rid = 'win-x64';   $Ext = 'zip' }
elseif ($arch -eq 'Arm64') { throw "win-arm64 is not supported in v1" }
else { throw "Unsupported architecture: $arch" }
```

win-arm64 is explicitly not in the v1 platform list. If operators ask, it's an additive matrix row.

### 9.3 Download URL resolution

**Default: latest stable release.** The script uses `https://api.github.com/repos/mrbildo/collabhost/releases/latest` to resolve the latest tag, then constructs asset URLs from there.

**Optional pin via `--version` flag:**
```bash
install.sh --version v0.2.0
```
or
```powershell
.\install.ps1 -Version v0.2.0
```

When `--version` / `-Version` is set, the script skips the `latest` lookup and constructs URLs directly from the provided tag.

Asset URL template (R2 -- version in filename):
```
https://github.com/mrbildo/collabhost/releases/download/{tag}/collabhost-{version}-{rid}.{ext}
https://github.com/mrbildo/collabhost/releases/download/{tag}/collabhost-{version}-{rid}.{ext}.sha256
https://github.com/mrbildo/collabhost/releases/download/{tag}/checksums.txt
```

The `{version}` segment is the stripped semver (the tag without the `v` prefix). Install scripts construct it by passing the tag through `${TAG#v}` (bash) or `$Tag.TrimStart('v')` (PowerShell):

```bash
# install.sh
TAG="${TAG:-$(curl -fsSL https://api.github.com/repos/mrbildo/collabhost/releases/latest | jq -r .tag_name)}"
VERSION="${TAG#v}"
ARCHIVE="collabhost-${VERSION}-${RID}.${EXT}"
URL="https://github.com/mrbildo/collabhost/releases/download/${TAG}/${ARCHIVE}"
```

The rest of this section (checksum verification, extraction, PATH) references `$ARCHIVE` / `$VERSION` / `$RID` as defined here.

### 9.4 Flags

| Flag | Default | Purpose |
|------|---------|---------|
| `--version v<semver>` / `-Version v<semver>` | latest stable | Pin to a specific release tag |
| `--install-path <path>` / `-InstallPath <path>` | `$HOME/.collabhost/bin` | Override install location |
| `--skip-path` / `-SkipPath` | unset | Skip shell RC modification for PATH |
| `--help` / `-Help` | | Print usage and exit |

Intentionally omitted for v1: `--quality` (staging/daily tracks), `--force` (overwrite without merge-safe check). Both are Aspire-style but we don't have a reason for them yet.

### 9.5 Checksum verification step

This is the step that closes the Collaboard gap.

**`install.sh`:**

```bash
# ... after downloading archive and checksums.txt ...

EXPECTED=$(grep "  ${ARCHIVE}\$" checksums.txt | awk '{print $1}')
if [ -z "$EXPECTED" ]; then
  echo "Could not find checksum for ${ARCHIVE} in checksums.txt" >&2
  exit 1
fi

if command -v sha256sum >/dev/null 2>&1; then
  ACTUAL=$(sha256sum "$ARCHIVE" | awk '{print $1}')
else
  ACTUAL=$(shasum -a 256 "$ARCHIVE" | awk '{print $1}')
fi

if [ "$EXPECTED" != "$ACTUAL" ]; then
  echo "Checksum mismatch for ${ARCHIVE}" >&2
  echo "  Expected: $EXPECTED" >&2
  echo "  Actual:   $ACTUAL" >&2
  exit 1
fi
```

**`install.ps1`:**

```powershell
$expected = (Get-Content $ChecksumsFile | Select-String "  $Archive$" | Select-Object -First 1) -split '\s+' | Select-Object -First 1
if (-not $expected) {
    throw "Could not find checksum for $Archive in checksums.txt"
}

$actual = (Get-FileHash -Algorithm SHA256 -Path $Archive).Hash.ToLower()
if ($expected -ne $actual) {
    throw "Checksum mismatch. Expected: $expected Actual: $actual"
}
```

If verification fails, the script stops before extracting -- the corrupted download never reaches the user's install directory.

### 9.6 Extraction target and PATH integration

**Target:** `$HOME/.collabhost/bin` by default. No admin/root required. This mirrors Aspire's `$HOME/.aspire/bin`.

**`install.sh` PATH integration:**

```bash
# Detect shell RC file
RC_FILE=""
case "${SHELL:-}" in
  */zsh)  RC_FILE="$HOME/.zshrc" ;;
  */bash) RC_FILE="$HOME/.bashrc" ;;
  *)      RC_FILE="$HOME/.profile" ;;
esac

PATH_LINE='export PATH="$HOME/.collabhost/bin:$PATH"'

if [ -z "$SKIP_PATH" ] && ! grep -Fq "$PATH_LINE" "$RC_FILE" 2>/dev/null; then
  echo "" >> "$RC_FILE"
  echo "# Added by collabhost installer" >> "$RC_FILE"
  echo "$PATH_LINE" >> "$RC_FILE"
  echo "Added Collabhost to PATH in $RC_FILE"
  echo "Open a new terminal or run: source $RC_FILE"
fi
```

**`install.ps1` PATH integration:**

```powershell
$InstallBin = Join-Path $InstallPath '.'
$UserPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
if (-not $SkipPath -and ($UserPath -split ';') -notcontains $InstallBin) {
    [Environment]::SetEnvironmentVariable('PATH', "$InstallBin;$UserPath", 'User')
    Write-Host "Added Collabhost to User PATH. Open a new terminal for it to take effect."
}
```

PowerShell's User PATH modification is non-interactive and requires no admin. The variable applies to new processes.

### 9.7 Merge-safe update behavior

**Preserve on re-run:**
- `data/` directory (and everything inside) -- the SQLite database, any user-type JSONs once they've been synced, logs (if adopted -- §17.2 Q3).

**Overwrite on re-run:**
- `collabhost` binary
- `caddy` binary
- `appsettings.json` (shipped defaults) -- see the "open design tension" note below. Operator overrides in env vars (§12.3) are persistent and not affected by reinstall.
- `INSTALL.md`
- `LICENSES/`

**Not present in production layout:** `appsettings.Local.json` (dev-only convention, §2.5). The install script does not need to preserve it because production installs shouldn't have one.

**`install.sh`:**

```bash
# After verify, extract to a temp directory
TMP_EXTRACT=$(mktemp -d)
tar -xzf "$ARCHIVE" -C "$TMP_EXTRACT"

mkdir -p "$INSTALL_PATH"

# The archive extracts to collabhost-{version}-{rid}/... — descend into it
ARCHIVE_ROOT="$TMP_EXTRACT/collabhost-${VERSION}-${RID}"

# Overwrite files that are part of the bundle
cp "$ARCHIVE_ROOT/collabhost"         "$INSTALL_PATH/" 2>/dev/null || true
cp "$ARCHIVE_ROOT/caddy"              "$INSTALL_PATH/" 2>/dev/null || true
cp "$ARCHIVE_ROOT/appsettings.json"   "$INSTALL_PATH/"
cp "$ARCHIVE_ROOT/INSTALL.md"         "$INSTALL_PATH/"
mkdir -p "$INSTALL_PATH/LICENSES"
cp "$ARCHIVE_ROOT/LICENSES/"*         "$INSTALL_PATH/LICENSES/"

# Preserve data/ -- never copy it from the archive (it isn't in the archive anyway).
# Just leave it alone on disk.

chmod +x "$INSTALL_PATH/collabhost" "$INSTALL_PATH/caddy" 2>/dev/null || true
rm -rf "$TMP_EXTRACT"

# Auto-clear macOS quarantine attribute so the binary launches cleanly
if [ "$(uname -s)" = "Darwin" ]; then
  xattr -d com.apple.quarantine "$INSTALL_PATH/collabhost" 2>/dev/null || true
  xattr -d com.apple.quarantine "$INSTALL_PATH/caddy"      2>/dev/null || true
  echo "Cleared macOS quarantine attribute on collabhost and caddy."
fi
```

**`install.ps1`:** same shape, with `Expand-Archive` and `Copy-Item`. (No xattr step on Windows.)

The critical property: the archive never contains `data/`, so there's nothing to accidentally overwrite. We just don't touch it on disk. Merge-safe by construction.

**Open design tension: `appsettings.json` on reinstall.** The single-file production model (§2.5) says operators can edit `appsettings.json` in place *or* use env vars. The installer has to choose one of two behaviors, and they're in tension:

- **Option A -- Always overwrite.** Every reinstall brings in the new shipped defaults; operator edits to the file are lost. Safe for the "I only use env vars" operator, destructive for the "I edited the file" operator. Matches Aspire's model.
- **Option B -- Preserve if-exists.** If `appsettings.json` already exists at the install path, don't overwrite. Protects operator edits but means reinstalls never pick up new shipped defaults (e.g., a new key added in v0.2.0 with a sensible default).

R2 leans **Option A + loud warning in installer stdout and INSTALL.md** -- env vars are the blessed override mechanism, and the shipped defaults need to evolve. But this is a judgment call that affects the operator contract and I want Bill's confirmation before Phase 5 implementation. See §17.2 Q7.

The critical property that is not in tension: `data/` (the SQLite DB, user-type JSONs once DB-seeded, logs) is never touched by the installer. The archive doesn't contain `data/`. Merge-safe for operator data, by construction.

### 9.8 Uninstall

Consistent with Aspire: **no uninstall script.** The script prints:

```
Installed to $HOME/.collabhost/bin
To remove: rm -rf $HOME/.collabhost
To remove data too: also delete any ./data directory next to where you ran collabhost
```

Operators who set up data dirs outside `$HOME/.collabhost` are expected to remember. If this friction becomes real, we add uninstall in a follow-up.

---

## 10. GitHub Pages Setup

### 10.1 Repo settings change

In repo Settings -> Pages:
- **Source:** Deploy from a branch
- **Branch:** `main`
- **Folder:** `/docs`

After enabling, GitHub provisions `https://mrbildo.github.io/collabhost/` and serves every file in `docs/` at that URL root.

### 10.2 File layout under `docs/`

After this card ships:

```
docs/
├── install.sh                   [install script, Linux/macOS]
├── install.ps1                  [install script, Windows]
├── index.html                   [landing page, minimal]
├── screenshots/                 [existing]
├── social-preview.html          [existing]
└── social-preview.png           [existing]
```

### 10.3 Stable URLs

- `https://mrbildo.github.io/collabhost/install.sh`
- `https://mrbildo.github.io/collabhost/install.ps1`
- `https://mrbildo.github.io/collabhost/` -- landing

README (post-#155) and INSTALL.md point at these URLs.

### 10.4 Minimal `docs/index.html`

**Recommendation: yes, ship a minimal landing page.** Nothing fancy, one screen. Reason: when someone lands on `mrbildo.github.io/collabhost` (e.g., because they've seen `collabhost.dev` linked and clicked through), a blank 404 feels amateur. A minimal page with the install command and a link to the GitHub repo sets the tone.

Content sketch (actual styling deferred to Dana if she wants input):

```html
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Collabhost</title>
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <style>
    body { font-family: system-ui, sans-serif; max-width: 720px; margin: 3rem auto; padding: 0 1rem; line-height: 1.5; }
    pre { background: #f4f4f4; padding: 1rem; overflow-x: auto; }
    code { font-family: ui-monospace, monospace; }
  </style>
</head>
<body>
  <h1>Collabhost</h1>
  <p>A self-hosted application platform. Process supervision, routing, and an operator dashboard for the services you run on your own machine.</p>
  <h2>Install</h2>
  <p>Linux / macOS:</p>
  <pre><code>curl -fsSL https://mrbildo.github.io/collabhost/install.sh | bash</code></pre>
  <p>Windows:</p>
  <pre><code>iwr -useb https://mrbildo.github.io/collabhost/install.ps1 | iex</code></pre>
  <p><a href="https://github.com/mrbildo/collabhost">Source on GitHub</a></p>
</body>
</html>
```

### 10.5 Verification after first enable

1. Enable Pages in repo settings.
2. Push the commit containing `docs/install.sh` + `docs/install.ps1` + `docs/index.html`.
3. Wait for GitHub Pages build to complete (~1 minute, visible in Settings -> Pages).
4. `curl -I https://mrbildo.github.io/collabhost/install.sh` -> expect 200 + correct `Content-Type` (`application/x-sh` or `text/plain`, either is fine).
5. Check that the script content matches what's in the repo (`curl ... | diff - docs/install.sh`).
6. Same for `install.ps1`.
7. Hit the landing page in a browser.

### 10.6 Future CNAME

Decision 12 notes that `docs/CNAME` can later redirect to `collabhost.dev` when Bill buys the domain. The file is a one-line `collabhost.dev`. When ready:
1. Buy `collabhost.dev`.
2. Add DNS CNAME: `collabhost.dev` -> `mrbildo.github.io`.
3. Commit `docs/CNAME` with contents `collabhost.dev`.
4. GitHub Pages auto-switches to serving at `https://collabhost.dev/`.
5. Install scripts and README get a URL find-replace pass.

Not in this card's scope. Listed here so the eventual upgrade is clear.

---

## 11. macOS Gatekeeper Handling

### 11.1 INSTALL.md section content (exact)

Ship inside the archive under `INSTALL.md`:

```markdown
## macOS: first-run quarantine

macOS attaches a quarantine attribute to binaries downloaded from the internet.
Without removing it, you will see "`collabhost` cannot be opened because the
developer cannot be verified" the first time you run it.

Because Collabhost's binaries are not notarized in v1, you need to clear the
attribute manually. From the directory where you extracted the archive:

    xattr -d com.apple.quarantine collabhost
    xattr -d com.apple.quarantine caddy

Why: Apple requires an Apple Developer Program enrollment (and a per-release
notarization step) before binaries launch cleanly. We're skipping that for v1
to avoid the $99/year enrollment friction. If Collabhost's macOS usage grows
enough to warrant it, we will notarize in a later release.

The `install.sh` script runs these `xattr` commands for you automatically
and prints a line saying so. This note is for operators who download and
extract the archive manually.
```

### 11.2 Auto-clear in install.sh -- locked (R2)

Bill ruled: `install.sh` runs `xattr -d` automatically on macOS after extraction, **and prints a confirmation line** so operators know the attribute was cleared.

Implementation (finalized in §9.7's install.sh block):

```bash
if [ "$(uname -s)" = "Darwin" ]; then
  xattr -d com.apple.quarantine "$INSTALL_PATH/collabhost" 2>/dev/null || true
  xattr -d com.apple.quarantine "$INSTALL_PATH/caddy"      2>/dev/null || true
  echo "Cleared macOS quarantine attribute on collabhost and caddy."
fi
```

`|| true` because `xattr -d` errors if the attribute isn't set (e.g., on a re-run).

INSTALL.md updates: "The `install.sh` script runs these commands for you automatically. This note is for operators who download and extract the archive manually."

---

## 12. Configuration resolution: settings surface and env-var overrides

This section replaces R1's "env var floor" framing. The resolution model is §2.5 (single-pass, `CLI > env > appsettings.json > default`). Here we enumerate which production settings get which env-var overrides, grounded in the **settings audit** (`.agents/temp/settings-audit-shipped.md`) produced during #159.

### 12.1 What the audit found

The audit catalogued the production-API settings surface by category. After filtering out Aspire orchestration, dev launch profiles, and frontend concerns, **7 operator-configurable production settings** remained, plus the admin key as a documented special case:

| # | Setting | Source today | Default | Read site |
|---|---------|--------------|---------|-----------|
| 1 | SQLite connection string | `ConnectionStrings:Host` | `"Data Source=./db/collabhost.db"` | `Data/_Registration.cs` |
| 2 | User types directory | `TypeStore:UserTypesDirectory` | `"UserTypes"` (relative to `AppContext.BaseDirectory`) | `Data/AppTypes/_Registration.cs` |
| 3 | Proxy base domain | `Proxy:BaseDomain` | `"collab.internal"` | `Proxy/_Registration.cs` |
| 4 | Caddy binary path | `Proxy:BinaryPath` | `"caddy"` (R2: becomes empty; bundled default per §6.4.1) | `Proxy/_Registration.cs` |
| 5 | Caddy listen address | `Proxy:ListenAddress` | `":443"` | `Proxy/ProxyConfigurationBuilder.cs` |
| 6 | TLS cert lifetime | `Proxy:CertLifetime` | `"168h"` | `Proxy/ProxyConfigurationBuilder.cs` |
| 7 | Self port (Caddy → API reverse-proxy target) | `Proxy:SelfPort` | `58400` | `Proxy/ProxyConfigurationBuilder.cs` |
| special | Admin key | `Auth:AdminKey` (config) / (#158 TBD, `--admin-key` CLI arg floated) | `null` → runtime-generated ULID, logged | `Authorization/_Registration.cs` |

Also present and operator-relevant, but **framework-managed, not Collabhost-specific:**

- `ASPNETCORE_ENVIRONMENT` -- standard .NET env var. Affects migration/seed gating **until #156 lands** (which removes the dev-only gate entirely).
- `Logging:LogLevel:*` -- standard ASP.NET Core logging configuration.
- `OTEL_EXPORTER_OTLP_ENDPOINT` -- standard OpenTelemetry env var; OTLP export is a no-op when empty.

This spec does NOT introduce env-var names that shadow the standard ones. `ASPNETCORE_ENVIRONMENT`, `Logging:*`, and `OTEL_EXPORTER_OTLP_ENDPOINT` are operator knobs today via ASP.NET Core's built-in mechanism and we leave them alone.

**Dropped entries.** `Platform:ToolsDirectory` appeared in `appsettings.json` but is **not read by any C# code.** The audit flagged it as dead config. It is being removed as part of #156's hygiene pass, and therefore `COLLABHOST_TOOLS_PATH` (which R1 proposed) **is not in R2's env-var list**.

### 12.2 Default data path change

Today `appsettings.json` defines:

```json
"ConnectionStrings": { "Host": "Data Source=./db/collabhost.db" }
```

**Change (R2):** default moves from `./db/collabhost.db` to `./data/collabhost.db` to match locked decision 14. This is a breaking change for local dev databases (which re-seed from migrations anyway, harmless) and a clear release-notes item for any early adopter running an older build.

Both `./db/` and `./data/` resolve relative to `AppContext.BaseDirectory`, which is the directory containing the `collabhost` binary. The install layout is `$HOME/.collabhost/bin/collabhost` + `$HOME/.collabhost/bin/data/collabhost.db`. The operator's `data/` directory lives next to the binary by default.

### 12.3 Env-var overrides shipped in v1

**The canonical list:**

| Env var | Overrides | Shape |
|---------|-----------|-------|
| `COLLABHOST_DATA_PATH` | Effective parent directory for SQLite DB (connection string derived from this) | Absolute directory path |
| `COLLABHOST_USER_TYPES_PATH` | `TypeStore:UserTypesDirectory` | Absolute directory path |
| `COLLABHOST_CADDY_PATH` | Used by `CaddyResolver` (§6.4.1) at precedence 1 | Absolute file path |

**What's deliberately not in the list:**
- `COLLABHOST_TOOLS_PATH` (R1 proposal) -- the setting it would override is dead config being removed.
- `COLLABHOST_TEMP_PATH` (R1 proposal) -- the temp path is `Path.GetTempPath()/collabhost/`. OS-standard temp handling is correct here; introducing a project-specific env-var override for scratch files adds a surface without a real user need. Revisit if an operator surfaces a real use case.
- Proxy-specific knobs (`COLLABHOST_PROXY_LISTEN_ADDRESS`, `COLLABHOST_BASE_DOMAIN`, `COLLABHOST_PROXY_SELF_PORT`, `COLLABHOST_CERT_LIFETIME`): these are `appsettings.json` entries today. An operator with a good reason to change them can edit the shipped file. Env-var duplicates add noise for settings that almost never change at deployment time. Added to the §17.2 open-questions list as "should any of these get an env-var override?" for Bill's call before Phase 3 ships.

### 12.4 Why explicit top-level env vars, not ASP.NET Core double-underscore nesting

ASP.NET Core's configuration pipeline supports env-var-as-config via `AddEnvironmentVariables(prefix: "COLLABHOST_")`, after which `COLLABHOST_Proxy__BinaryPath` becomes equivalent to `Proxy:BinaryPath`. That's the idiomatic path.

**Recommendation (unchanged from R1): explicit top-level env vars**, read directly in the subsystem registration code. Three reasons:

1. **Operator-friendly naming.** `COLLABHOST_DATA_PATH` is easier to discover and type than `COLLABHOST_ConnectionStrings__Host`. Operators are reading INSTALL.md, not memorizing internal config schema.
2. **Internal keys stay internal.** If we refactor `ConnectionStrings:Host` → `Data:DatabasePath` later, operator env vars don't break.
3. **Explicit call sites.** `CaddyResolver` reads `COLLABHOST_CADDY_PATH` at one site. No magic key translation at startup. Easy to `grep`.

Each subsystem that honors an env var owns one lookup, placed in its `_Registration.cs`:

```csharp
// Example: Data/_Registration.cs
var dataPath = Environment.GetEnvironmentVariable("COLLABHOST_DATA_PATH");
var connectionString = !string.IsNullOrWhiteSpace(dataPath)
    ? $"Data Source={Path.Combine(dataPath, "collabhost.db")}"
    : configuration.GetConnectionString("Host")
        ?? "Data Source=./data/collabhost.db";
```

Precedence per §2.5: env → config → default. CLI args slot in on top when #153-successor work introduces them.

### 12.5 Scope relative to #104

#104 was R1's informal "config layering" card. Post-#159, **#104's scope is narrower**: it owns the *runtime-mutable* / UI-editable / hot-reload configuration surface, which is a different bag entirely (DB-backed, no `appsettings.json` write-back). The 7 settings and admin key in §12.1 are load-time and out of #104's scope. This spec ships the env-var overrides for the three paths above and is complete for v1; #104 is independent.

---

## 13. INSTALL.md Contents Outline

Target length: ~4-5 pages (roughly 150-200 lines of Markdown). Shipped inside the archive; also visible on GitHub for the repo. The R1 outline was expanded after Dana's review -- more positive-signal content, admin key promoted, "where's my dashboard" questions answered up front.

### 13.1 Section list (R2, ordered for the journey)

The INSTALL.md reader is **post-download, pre-success**. Their goal is "get from extracted archive to I can see my dashboard in the browser." Sections are ordered by what blocks that journey:

1. **Quick start** -- 3 commands: extract, run, open browser. Target: 60 seconds from top of page to dashboard.
2. **Your admin key** (R2 top-level promotion per Dana) -- where it appears on first run, how to capture it, what to do if you missed it. Cross-reference to #156's behavioral model + #158's UX decisions when they land.
3. **Opening the dashboard** (R2 new) -- exact URL (`http://localhost:58400`), what the operator should see, what a healthy first-load looks like.
4. **What's in this archive** -- file list with one-line purpose. Mirrors §5.2.
5. **Configuration** (R2 reframed) -- the single-file `appsettings.json` model (§2.5), env-var overrides from §12.3, precedence, which settings are operator-knobs vs framework-standard.
6. **macOS: first-run quarantine** -- the exact text from §11.1. Primary audience is the manual-download operator; `install.sh` runs `xattr -d` automatically (§11.2).
7. **Verifying the install** (R2 new per Dana) -- the positive signals:
   - `collabhost --version` prints `Collabhost 0.1.0`.
   - `curl http://localhost:58400/api/v1/status` returns JSON with `"status": "running"` and `proxyState` (see §6.4.2).
   - Dashboard loads.
8. **Updating** -- "re-run the install script; `data/` is preserved." Call out the `appsettings.json` behavior explicitly (pending §17.2 Q7).
9. **Troubleshooting** -- expanded list:
   - **Caddy did not start** (`proxyState` in `/status` = `"failed"`). Try setting `COLLABHOST_CADDY_PATH` to a known-working Caddy, or check bundled binary permissions.
   - **Port 443 already in use** (another proxy or service on the host). Edit `Proxy:ListenAddress` in `appsettings.json`.
   - **Port 58400 already in use** (another app on the API port). Edit `Proxy:SelfPort` in `appsettings.json` and the actual API listen port.
   - **collabhost won't launch on macOS** -- link back to §11.
   - **SQLite file permission errors on Linux** -- check `$HOME/.collabhost/bin/data/` ownership.
   - **Admin key missing from scrollback** (Dana R1) -- recovery path is TBD pending #156/#158. Placeholder section with "see GitHub release notes for {version}" until the cards land.
   - **Binary crashes before I see anything** -- `data/logs/` path (if adopted per Dana's friction-point 5; open question §17.2 Q8).
10. **Uninstall** -- `rm -rf $HOME/.collabhost`.
11. **Verifying checksums** -- for manual downloaders. Examples with versioned filename: `sha256sum -c collabhost-0.1.0-linux-x64.tar.gz.sha256`.
12. **Environment variables reference** (R2 new) -- canonical list from §12.3, with shape (file vs directory) and example values.

### 13.2 What goes where (INSTALL.md vs README post-#155)

INSTALL.md is for the **already-downloaded** operator (practical, concrete, local). README is for the **first-time reader** (conceptual, "why Collabhost exists," then links to install). Duplication is acceptable where the content is a single bullet (install command). If it's a paragraph or more, INSTALL.md owns it and README links.

Linked from README post-#155: yes, from the "Installation" section.

### 13.3 Contents pending prerequisite cards

Two INSTALL.md sections have placeholder status until their source cards land:

- **Admin key** -- the exact message format, the recovery flow, and the capture location (stdout alone vs stdout + file vs prompt) all depend on #156 (behavior) and #158 (UX research). R2 INSTALL.md draft uses a `{PLACEHOLDER}` block with a TODO comment pointing at the cards, and the final wording is filled in when those cards resolve. **This is a blocker for shipping v0.1.0 -- not for merging this spec.**
- **Post-install log location** -- depends on whether Dana's "logs to `data/logs/`" proposal is adopted. See §17.2 Q8.

---

## 14. Version Endpoint & CLI Flag -- Existing Code Review

### 14.1 Where version is read today

Three call sites read `AssemblyInformationalVersionAttribute`:

| File | Line | Purpose |
|------|------|---------|
| `backend/Collabhost.Api/System/SystemEndpoints.cs` | 22 | `/api/v1/status` response |
| `backend/Collabhost.Api/Mcp/DiscoveryTools.cs` | 61 | MCP `get_system_status` response |
| `backend/Collabhost.Api/Mcp/_McpRegistration.cs` | 14 | MCP server `Implementation.Version` field |

None of them strip a commit-hash suffix. If the SDK ever adds one (it does, when `SourceLink` is active, as `+<hash>`), all three leak it.

### 14.2 Where to put the helper

`backend/Collabhost.Api/Platform/VersionInfo.cs` (new). Static class, single `Current` property, `Lazy<string>`. Call sites shrink from three lines to one.

Why `Platform/`: that namespace already owns `SystemEndpoints.cs`, `SystemStatus.cs`, and `Platform/_Registration.cs`. Version is a platform concern.

### 14.3 Where the `--version` flag goes

At the top of `Program.cs`, before `WebApplication.CreateBuilder(args)`. The assembly attributes are available at that point because they live on the compiled assembly, not on the runtime host. Showed the ~6-line insertion in §8.5 (R2 format: `Collabhost {version}`).

### 14.4 Side effect on /api/v1/status

`SystemEndpoints.GetStatus` currently embeds a raw `InformationalVersion` read. Replace with `VersionInfo.Current`. The `SystemStatus` response shape gains one new field in R2 -- `proxyState` (§6.4.2) -- but the version field remains. One test to update (`SystemEndpointsTests` or equivalent -- if one exists) for the stripped-commit-hash behavior, and one new test for `proxyState`.

### 14.5 A small cleanup opportunity

The MCP `DiscoveryTools` reads version for `get_system_status`. The MCP `_McpRegistration` reads it for `Implementation.Version`. These are two reads of the same value, both at runtime. Neither is hot. After this refactor, both just call `VersionInfo.Current`. Won't touch caching -- `Lazy<string>` already handles it.

### 14.6 Scope boundary with #156

R1 of this spec proposed moving `ProxyAppSeeder.SeedAsync` out of `Program.cs`'s `IsDevelopment()` block. Marcus's review surfaced that migrations, TypeStore load, and the proxy seeder are all in that block -- and that lifting the gate without a backup/data-safety discipline risks operator data loss on production upgrade.

**That entire concern is now card #156.** Not this card. This spec:

- Declares #156 a **hard prerequisite** for any production deployment of Collabhost.
- Does not specify migration behavior, backup logic, or the seeding contract. Those are #156's deliverables (spec at `.agents/specs/production-startup.md`, per #156 acceptance criteria).
- Does not rewrite `Program.cs`'s startup block.
- Inherits from #156 whatever production posture the design lands on. This spec's Phase 4 (workflow + bundle) cannot ship a working production release until #156 is implemented.

Concretely: the phased implementation (§18) sequences #156's work before Phase 4 of #153. If #156 slips, #153 Phase 4 slips with it -- but Phases 1-3 of #153 can still merge independently.

---

## 15. Test Strategy

Four categories: version injection, Caddy binary resolution + post-launch probe, checksum format, workflow end-to-end.

### 15.1 Unit tests (new)

**`VersionInfoTests`:**
- `Current_WithoutCommitHash_ReturnsRawValue` -- given no `+`, returns as-is.
- `Current_WithCommitHash_StripsSuffix` -- `0.1.0+abc123` -> `0.1.0`.
- `Current_EmptyAttribute_ReturnsDefault` -- handles missing attribute by returning `0.0.0`.

These are testable via reflection against a stub assembly attribute, but simplest is to extract the strip logic into a pure method (`StripCommitHash(string raw)`) and unit-test that directly. `Current` is a thin Lazy wrapper.

**`CaddyResolverTests`:** (precedence chain per §6.4.1, env > config > bundled)
- `Resolve_EnvVarSetAndExists_ReturnsEnvVarPath`
- `Resolve_EnvVarSetButMissingFile_LogsWarningAndFallsThrough`
- `Resolve_ConfigBinaryPathAbsoluteAndExists_ReturnsConfigPath`
- `Resolve_ConfigBinaryPathBareNameResolvesViaPath_ReturnsResolvedPath`
- `Resolve_ConfigBinaryPathSetButUnresolvable_FallsThroughToBundled`
- `Resolve_BundledSidecarExists_ReturnsBundledPath` -- with a temp-dir fake `AppContext.BaseDirectory`.
- `Resolve_AllPathsExhausted_ReturnsNull`

Note: no `ExistingCaddyProbeTests` in R2. R1's Probe A is dropped (§6.4), so the test class is not needed.

**`ProxyManagerVerifyCaddyReadyTests`:** (post-launch probe with soft-fail)
- `VerifyCaddyReady_HealthyImmediately_ReturnsTrue` -- local HTTP listener returns 200 on first call.
- `VerifyCaddyReady_SlowStart_ReturnsTrueWithinDeadline` -- listener returns 503 for 2s, then 200. Assert true within 5s.
- `VerifyCaddyReady_NeverReady_ReturnsFalseAndProxyStateFailed` -- listener returns 503 forever. Assert false, assert `ProxyManager.CurrentState == "failed"` after call.
- `VerifyCaddyReady_HangingConnection_TimesOutAndRetries` -- listener accepts but never responds. Assert per-attempt timeout kicks in (1s), loop continues, eventually returns false at 5s.

**`SystemStatus` proxyState field:**
- `GetStatus_ProxyRunning_ReturnsRunning`
- `GetStatus_ProxyFailed_ReturnsFailed`
- `GetStatus_ProxyDisabled_ReturnsDisabled`
- `GetStatus_ProxyStopped_ReturnsStopped`

**CLI `--version` flag:**
- Integration-ish test in `Collabhost.Api.Tests`: launch the published binary (or the built assembly) with `args = ["--version"]`. Assert stdout equals `Collabhost {VersionInfo.Current}\n` and exit code is 0. Because `Program.cs` short-circuits, the web host never starts.

### 15.2 Integration tests (existing + updates)

Existing `ProxyAppSeederTests` need updates for the reshaped binary resolution. The three current tests (`ResolveBinaryPath_BareName_ResolvesFromPath`, etc.) still apply to the legacy path but the priority-chain above it is new.

Update list:
- `ProxyAppSeederTests` -- verify the seed still works when Caddy is available at the bundled location, env-var location, and legacy config.
- `ProxyArgumentProviderTests` -- unchanged; the argument provider is unaffected by the resolver change.
- `CaddyClientTests` -- if they exist; if not, add one that asserts `IsReadyAsync` returns true/false based on a local test HTTP listener.
- Tests that construct `ProxySettings` with `BinaryPath = "caddy"` -- now the resolution chain does not require that setting to be non-empty. Update fixtures.

### 15.3 What to verify in the CI workflow itself

- Version injection: build, then `./publish/collabhost-0.1.0-linux-x64/collabhost --version` → expect `Collabhost 0.1.0`.
- Caddy resolver + probe: no CI test covers this directly because it requires a running Caddy. Covered by unit tests at the resolver + probe level (§15.1).
- Checksum format: build archive, `sha256sum -c collabhost-0.1.0-linux-x64.tar.gz.sha256` should succeed.
- `proxyState` field on `/status`: can be covered by an `IntegrationTest` spinning up the `WebApplicationFactory` with a fake `CaddyClient` that returns unhealthy. Assert `/status.proxyState == "failed"`.

These are scriptable as a final "self-check" step in the matrix leg if we want belt-and-suspenders:

```yaml
      - name: Self-check
        shell: bash
        env:
          VER: ${{ needs.extract-version.outputs.version }}
        run: |
          sha256sum -c "collabhost-${VER}-${{ matrix.rid }}.${{ matrix.ext }}.sha256"

          # Version check requires extract + run; skip for RIDs that won't run on the CI host.
          if [ "${{ matrix.rid }}" = "linux-x64" ] && [ "$RUNNER_OS" = "Linux" ]; then
            mkdir -p verify
            tar -xzf "collabhost-${VER}-linux-x64.tar.gz" -C verify
            # Expected stdout: "Collabhost <semver>"
            ACTUAL=$(./verify/collabhost --version)
            EXPECTED="Collabhost ${VER}"
            if [ "$ACTUAL" != "$EXPECTED" ]; then
              echo "Version mismatch. Expected: '$EXPECTED', Got: '$ACTUAL'" >&2
              exit 1
            fi
          fi
```

The self-check gates a bad build before it hits the Release. Recommend including.

### 15.4 End-to-end smoke against a test tag

**Question: can we run the publish workflow against a test tag / PR artifact?**

Yes, with a caveat. The workflow is triggered by `release: [published]`, and GitHub's release event only fires on actual releases. To dry-run:

Option A (recommended): **duplicate the matrix job into a separate `publish-dryrun.yml` triggered on `workflow_dispatch`** with an input for a fake version, wiring in a mock "tag" and skipping the `gh release upload` step. This lets maintainers test changes without cutting a real release. Low cost; kept in sync with the real workflow via shared action logic (composite action under `.github/actions/`).

Option B: cut pre-release tags (e.g., `v0.0.1-test.1`) and hit the real release event. Works, but pollutes the Releases tab and -- importantly -- decision 5 says no pre-release conventions in v1. So we'd have to relax the regex in `extract-version` just to run dry-runs. Not great.

**Recommendation: Option A.** Add `publish-dryrun.yml` in a follow-up (not necessarily part of this card's first merge). Initial verification can be "cut a v0.1.0 and fix forward."

---

## 16. Release Cycle Playbook

### 16.1 Operator workflow to cut a release

Prerequisites:
- `main` is green on CI.
- The commit to release is on `main`.
- The operator has `gh` authenticated and push rights.

Steps:

1. **Decide the version.** For the first release: `v0.1.0`. For a patch: bump the patch digit. Follow semver: breaking -> major, feature -> minor, bug -> patch.
2. **Tag and push:**
   ```bash
   git checkout main
   git pull
   git tag v0.1.0
   git push origin v0.1.0
   ```
3. **Create the Release.** Either:
   - GitHub UI: Releases -> Draft a new release -> select tag `v0.1.0` -> write notes -> Publish release.
   - CLI: `gh release create v0.1.0 --generate-notes` (auto-draft release notes from merged PRs).
4. **Watch the workflow.** Go to Actions -> Publish. Five matrix legs run in parallel (~3-5 min each). Total ~5-7 min wall clock.
5. **Verify artifacts.** After the workflow completes:
   - Go to the Release page.
   - Expect 5 archives (e.g. `collabhost-0.1.0-linux-x64.tar.gz`) + 5 per-archive `.sha256` files + 1 `checksums.txt`.
   - Download one at random, run `sha256sum -c collabhost-0.1.0-linux-x64.tar.gz.sha256`, verify.
6. **Smoke test.** On a test machine, run `install.sh` or `install.ps1` from the published URL. Run `collabhost`. Check `collabhost --version` prints `Collabhost 0.1.0`. Hit `/api/v1/status` and verify `proxyState: "running"`. Open the dashboard at `http://localhost:58400` and log in with the first-run admin key.
7. **Close out.** Post-release, update any cards blocked on the release. (For #153 specifically, close the card.)

### 16.2 Troubleshooting

**Workflow failed mid-matrix (one leg red, four green).**
- Go to the failed leg's logs.
- Common cause: Caddy download failed (transient GitHub 5xx). Re-run the failed leg only from the Actions UI.
- `gh release upload ... --clobber` means re-running is safe -- it overwrites, doesn't duplicate.

**Artifact missing from the Release page but workflow green.**
- Unlikely but possible. Check the final step of the matrix leg's log for `gh release upload` success.
- Manual fix: `gh release upload v0.1.0 collabhost-0.1.0-linux-x64.tar.gz --clobber` from a local checkout.

**Checksum mismatch reported by install script on a user's machine.**
- Almost certainly a corrupted download (partial CDN response, etc.).
- User re-runs the install script.
- If persistent, download the checksums.txt by hand and recompute -- if the checksum in the file doesn't match reality, we have a release-side bug (workflow race or partial re-upload). `gh release upload --clobber` to repair.

**Version mismatch between tag and runtime `--version`.**
- Means `/p:Version=` did not thread through. Check `extract-version` job outputs and the publish step env.

**Tag rejected by `extract-version`.**
- Tag does not match `^v\d+\.\d+\.\d+$`. Likely a typo or a pre-release tag (which v1 does not support). Delete the tag, re-tag correctly, re-publish.

### 16.3 Frequency expectation

Not prescribed here. Early (0.1.x) expect several releases per month as bugs shake out. Later, a "meaningful changes accumulate" cadence matching PocketBase / Vaultwarden. Specific release cadence is not a release-pipeline concern.

---

## 17. Risks & Open Questions

### 17.1 Risks (with mitigations)

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|-----------|
| Caddy download from GitHub fails mid-workflow | Medium | Low | `curl --retry 3 --retry-delay 5`. On persistent failure, re-run the matrix leg. |
| Caddy release removed or renamed by upstream | Low | High | Pinned version never moves without our action. But if Caddy ever purges old releases, we could break. Mitigation: add a monthly sanity check that `caddy.version`'s asset URLs still resolve. |
| CI self-check doesn't catch a broken single-file bundle | Medium | Medium | Add the §15.3 self-check as a workflow step. |
| Install script hosted on GitHub Pages becomes unavailable | Low | High | GitHub Pages has strong uptime. If this is ever a real worry, mirror the scripts to Cloudflare Pages as a backup. Not day-1. |
| Checksum verification fails because of download corruption | Medium | Low (user self-heals by re-running) | Install scripts exit with clear error; users re-run. |
| macOS Gatekeeper blocks binaries despite `xattr` cleanup | Low | Medium | INSTALL.md explains; install.sh auto-clears. If operators still hit it, they can dismiss via System Settings -> Privacy & Security. |
| Self-contained single-file bundle size too large | Medium | Low | Already called out in §5.3. Not blocking v1. `PublishTrimmed` available as a follow-up optimization. |
| Pinned Caddy has a CVE before we notice | Medium | Medium-High | Card #154 owns this process. We acknowledge the exposure here and push response logic out. |
| Tag-regex gate rejects a legitimate tag someone wants to ship | Low | Low | Error message is clear, fix is a single line in `publish.yml`. |
| **#156 does not land before Phase 4** | **Medium** | **Critical** | **Phase 4 cannot ship a working production release without #156's production startup posture. Phased implementation (§18) gates Phase 4 explicitly on #156 being merged. Phases 1-3 of #153 ship independently and derisk the work.** |
| `COLLABHOST_DATA_PATH` env var collides with a pre-existing user env var | Very Low | Low | Prefix `COLLABHOST_` is unique enough. |
| Binary rename (`Collabhost.Api` → `collabhost`) breaks local dev tooling or docs | Low | Low | `AssemblyName` override is publish-time only; `dotnet run` during dev still produces `Collabhost.Api.exe`. Docs pass in #155 updates any stale references. |
| Proxy `proxyState` field is new contract surface -- downstream consumers not yet updated | Medium | Low-Medium | Dana owns dashboard rendering; add field with default `"running"` fallback so legacy readers don't break. See §17.2 Q6. |

### 17.2 Open questions

**R1's six questions are all closed (§17.3).** The R2 list is new.

1. **Proxy `SelfPort` cross-validation.** Audit Observation O5: `Proxy:SelfPort` (58400 default) must match the actual API listen port, but nothing cross-validates them. If an operator changes the Kestrel listen port without updating `SelfPort`, the Caddy reverse-proxy back-route breaks silently. Propose: add a startup check that compares `ProxySettings.SelfPort` to the configured listen port and logs a warning on mismatch. Out of strict #153 scope -- file as a standalone card? Bill's call.

2. **Caddy download checksum verification inside the workflow.** §6.2 notes we skip this in v1. Caddy publishes SHA256 sums alongside each release. Adding verification is ~6 lines of bash per leg. My recommendation: add it to Phase 4 directly. Low cost, high safety when Caddy's GitHub Releases endpoint ever gets compromised or mirrored. Bill's call on scope creep.

3. **Log directory for pre-crash diagnostics.** Dana's R1 friction-point 5: if the binary crashes before the operator sees stdout, there's no log to read. Propose: write Collabhost's own stdout/stderr to `{DataPath}/logs/collabhost-{timestamp}.log` with rotation. This is a ~30-line addition to the Supervisor / Platform startup. In scope of #153? Or push to a follow-up card?

4. **Env-var overrides for `Proxy:*` settings.** §12.3 doesn't ship env vars for `BaseDomain`, `ListenAddress`, `CertLifetime`, `SelfPort`. These are edit-in-`appsettings.json` only. If Bill thinks any of these should have env vars in v1, add them to §12.3 -- one line each.

5. **`install.ps1` User PATH vs Machine PATH.** §9.6 writes to User PATH (no admin). Some operators will expect System PATH. Propose: stay User-only in v1 (matches `install.sh` `~/.bashrc` behavior). Revisit if operators ask.

6. **`proxyState` wire shape for null/unknown case.** The `SystemStatus` response currently returns data even if the proxy subsystem hasn't completed startup yet. If Collabhost is queried *during* startup (before `VerifyCaddyReady` completes), what should `proxyState` return? Propose: `"starting"` as a fifth value, with a narrow window (≤5s) where this is expected. Alternatively, `null` until probe completes. Dana's preference on which is more dashboard-friendly.

7. **Reinstall behavior for `appsettings.json`.** §9.7: install script overwrites the shipped `appsettings.json` on reinstall. Operator edits to that file are lost. Env vars (§12.3) are the persistent override path. This is the "single-file model" (§2.5) in action, but it's a change from the R1 "preserve appsettings.Local.json" framing. Want Bill's explicit sign-off since it affects the operator contract for upgrades.

8. **Pre-release / staging / daily tracks.** §9.4 says we don't support `--quality` (Aspire-style). Still holds? Once we ship v0.1.0, do we want a `v0.1.1-rc.1` path for dry-runs against real operators? (Dry-run workflow is #157's job. This is the tag-convention question, distinct from the workflow question.)

9. **`Collabhost.Api` → `collabhost` rename ripple effects.** §3.6 applies `-p:AssemblyName=collabhost` at publish. The published `collabhost.exe` has an internal `InternalsVisibleTo` that points at `Collabhost.Api.Tests` (or whatever the actual internals friend is). This still works because `InternalsVisibleTo` is per-assembly-name; our csproj is still `Collabhost.Api.csproj` so the internals assembly name is unchanged. But I want to verify this end-to-end at Phase 4 implementation time -- the combination of `AssemblyName` override + `InternalsVisibleTo` is an uncommon config.

### 17.3 R1 open questions -- closed status

| # | R1 Question | Resolution |
|---|-------------|------------|
| 1 | Hard-fail vs soft-fail on probe failure | **Soft-fail with visibility (`proxyState` in `/status` + dashboard signal).** See §6.4.2. Locked by Bill. |
| 2 | `--version` stdout format (bare vs labeled) | **`Collabhost {version}`** per Dana's argument. See §8.5. Locked by Bill. |
| 3 | Auto-clear `com.apple.quarantine` in install.sh | **Yes, with stdout confirmation.** See §9.7 and §11.2. Locked by Bill. |
| 4 | Dry-run workflow same PR or follow-up | **Follow-up card #157.** Locked by Bill. |
| 5 | Archive filename includes version | **Yes, no `v` prefix** (`collabhost-0.1.0-linux-x64.tar.gz`). See §4. Locked by Bill (with Dana's UX argument winning). |
| 6 | `/status` vs `/version` diagnostic split | **`/version` = version+commit+platform, `/status` = runtime state.** Locked by Bill. See §8.4. (Note: this spec's `proxyState` work in §6.4.2 makes `/status` even more clearly the runtime-state endpoint.) |

### 17.4 Codebase anomalies (standing list)

Confirmed or newly raised during R2:

- **Card #83 (Caddy admin port hardcoded to 2019) -- stale.** `Proxy/_Registration.cs:21` uses `PortAllocator.AllocatePort()` already. Port 2019 appears only in test fixtures and (R1) in the Probe A design (which is dropped in R2). Recommend closing #83 and updating CLAUDE.md's "Known Issues" in a trivial doc-only PR. Confirmed standing in R2.
- **`Platform:ToolsDirectory` is dead config.** No C# reader. Removal is owned by #156's hygiene pass. Not in #153's scope; do not reference in this spec's configuration examples.
- **`CaddyClient.cs:~32` uses a blanket `catch`.** Marcus's R1 finding. Narrow during Phase 2 when the post-launch probe becomes load-bearing. Listed in §6.5 affected files.
- **`ProxyManager.StartAsync` emits stale log guidance.** The "ensure the proxy binary is available" message should reference `COLLABHOST_CADDY_PATH` and the bundled fallback once R2 Phase 2 lands. Copy update folded into §6.5.
- **`TypeStore` file-watcher behavior change in production.** Marcus O6: Today the FSW is dev-only by accident of being under `IsDevelopment()`. #156 decides whether the watcher remains in production or if it's replaced with load-once-at-startup. Out of #153's scope; noted here so it's not missed in the #156 design session.
- **`AddEnvironmentVariables` prefix convention.** If Bill later decides he wants double-underscore nesting (`COLLABHOST_Proxy__BinaryPath`) *in addition to* the explicit reads in §12.4, that's an additive change at registration time: `builder.Configuration.AddEnvironmentVariables(prefix: "COLLABHOST_")`. Not proposed here but noted as the escape route if someone asks.

---

## 18. Implementation Plan

Phased to keep review and merge units manageable. Each phase is a candidate branch/card. Review gates noted between phases. R2 reorders Phase 2 and Phase 3 per Marcus's argument, and gates Phase 4 on **#156** landing first.

### Phase ordering (R2)

```
Phase 1 (version helper)  ──────────────────┐
                                            │
Phase 3 (env-var overrides + data path)  ──┤
                                            │
Phase 2 (Caddy resolver + probe + proxyState) ─┐
                                                │
                                      [#156 lands: production startup]
                                                │
Phase 4 (release workflow + bundle + checksums) ──┐
                                                    │
                                Phase 5 (install scripts + Pages) ──>  v0.1.0 release
```

Phases 1 and 3 are fully independent. Phase 2 reads `VersionInfo.Current` from Phase 1 (for log messages) and the `COLLABHOST_CADDY_PATH` reader from Phase 3. Phase 4 depends on everything above it **plus #156** (which is a separate card, not a phase here). Phase 5 runs against Phase 4's artifacts.

### Phase 1: Version helper + CLI flag + endpoint

**Branch:** `feature/153-01-version`
**Scope:**
- `Platform/VersionInfo.cs` (new).
- Update three existing call sites (`SystemEndpoints`, `DiscoveryTools`, `_McpRegistration`).
- `Program.cs` short-circuit for `--version` / `-v`, emitting `Collabhost {version}`.
- `GET /api/v1/version` endpoint.
- Unit tests for `VersionInfo`.
- Integration test for `--version` flag.

**Why first:** smallest change, unblocks the workflow's `-p:Version=` step, verifiable in isolation. Gives us something immediately useful even if the rest of the release stack slips.

**Review gate:** Marcus or Dana reviews the helper shape and the endpoint contract.

### Phase 3: Configuration resolution -- env-var overrides + data path

**Branch:** `feature/153-03-env-var-overrides`
**Scope:**
- Implement env-var readers for `COLLABHOST_DATA_PATH`, `COLLABHOST_USER_TYPES_PATH`, `COLLABHOST_CADDY_PATH` (§12.3).
- Move default SQLite path from `./db/collabhost.db` to `./data/collabhost.db`.
- Wire `COLLABHOST_CADDY_PATH` through `ProxyAppSeeder.ResolveBinaryPath` (existing code path) -- full `CaddyResolver` refactor lands in Phase 2 but the env-var read itself is additive.
- Update `Data/_Registration.cs`, `Data/AppTypes/_Registration.cs` for their respective env-var paths.
- Unit tests for each env-var reader (set → wins, unset → config-fallback, config-unset → hardcoded-default).

**Why second (was third in R1):** Marcus's argument -- Phase 3 is additive (env vars with no readers are inert until readers land), and it gives us an incrementally useful first merge (operators can override paths via env) without dragging the Caddy resolver change with it. Also decouples env-var-reading from resolver-priority-chain concerns: Phase 3 is "read env vars," Phase 2 is "apply precedence when multiple sources disagree."

**Review gate:** small footprint, Kai review. Precedence logic is locked in §2.5, so there's little to debate structurally.

### Phase 2: Caddy resolver + post-launch probe + `proxyState`

**Branch:** `feature/153-02-caddy-resolver`
**Scope:**
- `Proxy/CaddyResolver.cs` (new, §6.4.1 precedence chain). Reads env var set up in Phase 3.
- `ProxyAppSeeder` refactor to use `CaddyResolver`.
- Update `appsettings.json` to remove the `Proxy:BinaryPath = "caddy"` default (or set to empty).
- `ProxySettings.cs` -- relax `BinaryPath` from `required`.
- `Proxy/ProxyManager.cs` updates: `VerifyCaddyReady` + soft-fail with `CurrentState` property (§6.4.2).
- `System/SystemEndpoints.cs` -- add `proxyState` to the `SystemStatus` response.
- `Proxy/CaddyClient.cs` -- narrow the blanket catch (Marcus O5).
- Stale log-message update in `ProxyManager.StartAsync` (§17.4).
- Unit tests: `CaddyResolverTests`, `ProxyManagerVerifyCaddyReadyTests`, `SystemStatusProxyStateTests`.
- **No scope creep into the `IsDevelopment()` gate lifts** -- those belong to #156.
- **Probe A is NOT implemented** -- dropped per §6.4.

**Why third (was second in R1):** structural change to the proxy subsystem. Keeping it separate from the workflow PR means the review can focus on subsystem correctness, not CI plumbing.

**Review gate:** full architectural review -- Marcus. The precedence chain + soft-fail + `proxyState` are the most behavior-change-heavy pieces.

### [External] Card #156 lands: production startup posture

**Not a phase here -- a hard prerequisite card.**

Before Phase 4 ships:
- Migration posture is decided (auto-migrate vs explicit, backup discipline).
- `IsDevelopment()` gates in `Program.cs` are removed with the designed replacement.
- `ProxyAppSeeder.SeedAsync`, TypeStore load, migrations all run in production.
- INSTALL.md has a data-recovery section.
- Admin key behavior (3-scenario model) is implemented per #156.

Phase 4 of #153 cannot produce a working production release until #156 is merged. Phases 1-3 of #153 can merge independently of #156.

### Phase 4: Release workflow + Caddy bundle + checksums

**Branch:** `feature/153-04-publish-workflow`
**Prerequisite:** #156 merged. Phases 1-3 merged.
**Scope:**
- `.github/workflows/publish.yml` (new) -- versioned archive names, `-p:AssemblyName=collabhost`, native runners per leg.
- `caddy.version` file at repo root.
- `release-assets/INSTALL.md`, `release-assets/caddy-LICENSE`, `release-assets/caddy-NOTICE`.
- Caddy download SHA256 verification step (§17.2 Q2 -- recommend including).
- Optional: cut a sacrificial `v0.0.1-alpha.0` tag against a test branch for first-run E2E validation. (Dry-run workflow #157 replaces this later.)

**Why fourth:** depends on Phases 1-3 being merged (the workflow exercises the full stack) and #156 (the artifact the workflow produces must actually run in production).

**Review gate:** Marcus for workflow architecture, Dana for any user-facing language in INSTALL.md / installer output.

### Phase 5: Install scripts + GitHub Pages

**Branch:** `feature/153-05-install-scripts`
**Prerequisite:** Phase 4 merged (install scripts are meaningless without a release to download from).
**Scope:**
- `docs/install.sh`, `docs/install.ps1`, `docs/index.html`.
- Version-substituted URL templates (§9.3).
- Enable GitHub Pages in repo settings (manual op, not a PR).
- macOS auto-`xattr` block in `install.sh` with stdout confirmation line.
- Coordinated README pointer to install commands (#155).

**Review gate:** Dana for the landing page + installer stdout UX (Dana's R1 friction-points 1-5), Marcus for the script logic. Both scripts reviewed together since they must stay in sync.

### Post-merge -- v0.1.0 release

After all five phases + #156 merge:
1. Cut the `v0.1.0` tag and release. (§16.1.)
2. Smoke-test the install flow on all three OSes.
3. Verify dashboard loads, `proxyState` is `"running"`, admin key is captured per #156/#158.
4. Close card #153.
5. Notify anyone blocked on a working release pipeline (card #154 planning, card #155 README pass).

### Optional follow-up cards

- `publish-dryrun.yml` workflow -- **card #157, already filed.**
- Admin key UX research -- **card #158, already filed.**
- `dotnet tool install -g collabhost` NuGet publishing (decision 12 notes as v1+).
- `winget` manifest submission (decision 12 notes as v1+).
- Homebrew custom tap (out of v1 per decision 18).
- `COLLABHOST_CADDY_ADOPT_EXISTING=true` explicit opt-in for externally-managed Caddy (§6.4 -- post-Probe-A parking spot).

---

## Appendix A: One-page summary for review (R2)

- **Workflow:** `publish.yml` on `release: [published]`. Jobs: `extract-version` → `build-frontend` → `build-matrix (5 RIDs, native runners)` → `publish-checksums`.
- **Caddy:** pinned in `caddy.version` (recommend `2.11.2`). Downloaded per-platform in-workflow. Shipped in-archive with `caddy-LICENSE` + `caddy-NOTICE`. SHA256 verification of the download is an open recommendation (§17.2 Q2).
- **Archive:** `collabhost-{version}-{rid}.(zip|tar.gz)`. Contents: `collabhost[.exe]` (renamed from `Collabhost.Api[.exe]`) + `caddy[.exe]` + `appsettings.json` + `INSTALL.md` + `LICENSES/`.
- **Checksums:** per-archive `.sha256` + aggregated `checksums.txt`. Install scripts verify before extracting.
- **Version:** `v{semver}` tag → `-p:Version=` → one static helper (`Platform/VersionInfo`) → `GET /api/v1/version` + `--version` CLI flag. `--version` stdout format: `Collabhost 0.1.0` (locked).
- **Caddy resolution (R2):** `COLLABHOST_CADDY_PATH` > `Proxy:BinaryPath` from `appsettings.json` > bundled sidecar. **Probe A dropped.** Post-launch probe with per-attempt 1s / deadline 5s; soft-fail with visibility (`proxyState` in `/status`, dashboard signal).
- **Install scripts:** `docs/install.sh` + `docs/install.ps1`. Aspire model. `$HOME/.collabhost/bin`. `data/` preserved on reinstall; `appsettings.json` overwrite behavior is an open question (§17.2 Q7). Hosted on GitHub Pages. macOS auto-`xattr` with stdout confirmation.
- **Env-var overrides (R2, scoped to audit):** `COLLABHOST_DATA_PATH`, `COLLABHOST_USER_TYPES_PATH`, `COLLABHOST_CADDY_PATH`. `COLLABHOST_TOOLS_PATH` dropped (source config is dead). `COLLABHOST_TEMP_PATH` deferred (no real user need).
- **Configuration resolution model (locked on #159):** single-pass reduction at startup. Precedence `CLI > env > appsettings.json > default`. Single `appsettings.json` in production (no `.Production.json`, no `.Local.json`).
- **Admin key:** behavior owned by **#156**; UX surface owned by **#158**. This spec references, does not respecify.
- **Hard prerequisite:** **#156** (production startup posture). Without it, Phase 4 cannot produce a working production release.
- **Phased implementation:** 5 branches, most-to-least isolatable. Order: Phase 1 (version) → Phase 3 (env-var overrides, was 3 in R1) → Phase 2 (Caddy resolver + proxyState, was 2 in R1 → now gated on Phase 3) → **[#156 lands]** → Phase 4 (workflow) → Phase 5 (install scripts).

### R2 change highlights for reviewers

1. **"Escape hatch" vocabulary retired.** Replaced by "configuration resolution" / "precedence chain" per the locked model (§2.5).
2. **Probe A dropped entirely** (§6.4). No external-Caddy auto-adoption in v1.
3. **Soft-fail is now soft-fail + visibility** (§6.4.2). New `proxyState` field on `/status`.
4. **Archive filenames versioned** (§4). Binary renamed `collabhost[.exe]`.
5. **`--version` format is `Collabhost {semver}`** (§8.5).
6. **Env-var list scoped to the shipped settings audit** (§12). Dead `COLLABHOST_TOOLS_PATH` removed.
7. **#156 is a hard prerequisite** (§14.6 and phase plan). #153 does not specify production startup behavior.
8. **R1's 6 open questions closed; 9 new/carried-over questions in §17.2** -- most are minor, three (Q3 log dir, Q7 appsettings overwrite, Q2 Caddy checksum) deserve explicit calls before Phases 4-5.

---

*End of spec. R2 length: ~1500 lines. Ready for Marcus + Dana R2 review.*
