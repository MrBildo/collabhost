# Release Pipeline -- Spec

**Card:** #153 -- Implement "Release"
**Author:** Remy (backend lead)
**Status:** Draft for review (Marcus, Dana, Bill)
**Related cards:** #104 (config layering), #154 (CVE response), #155 (README restructure)
**Date:** 2026-04-17

---

## 1. Overview

This spec defines the v1 release pipeline for Collabhost: the CI workflow that produces platform-specific release archives, the conventions that govern their contents, and the install scripts that turn them into a working installation on an operator's machine.

The shape of the pipeline is: an operator publishes a GitHub Release at tag `v{semver}`, a matrix GitHub Actions workflow fires on `release: [published]`, builds the frontend once and reuses it across five platform legs, downloads a pinned Caddy binary per platform, runs a self-contained single-file `dotnet publish`, packages the result together with Caddy + license files + `INSTALL.md` into a platform-appropriate archive, computes SHA256 checksums, and attaches everything to the Release. Two install scripts (`install.sh`, `install.ps1`) -- hosted via GitHub Pages from `docs/` -- then give operators a one-command experience to download, verify, extract, and PATH-integrate the binary. The result is a single bundle per platform that an operator can unpack and run, with Caddy already present and wired up, and a predictable way to re-run the installer for future updates.

The bar is higher than Collaboard's shipped pipeline in four places: **one** frontend build shared across matrix legs, bundled Caddy with an escape hatch, SHA256 checksums and verification, and macOS Gatekeeper guidance shipped inside the archive. Everything else inherits Collaboard's "manual release trigger, self-contained single-file publish, GitHub Releases as hosting" skeleton.

---

## 2. Locked Decisions (reference)

These are fixed inputs. The spec designs around them -- it does not re-argue them.

| # | Decision |
|---|----------|
| 1 | Manual release trigger. Workflow fires on `release: [published]`. Operator creates the Release; the workflow attaches artifacts. |
| 2 | Five platforms: `win-x64`, `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`. |
| 3 | Publish flags: `--self-contained`, `PublishSingleFile=true`. No trim, no ReadyToRun. |
| 4 | Version from Git tag (`v{semver}`, `v` stripped). Exposed at runtime via `GET /api/v1/version` and `--version` CLI flag. Both strip commit-hash suffix. |
| 5 | Versioning baseline `v0.1.0`. No pre-release conventions in v1. |
| 6 | Frontend built once, shared across matrix via artifact upload/download. |
| 7 | Archive format: `.zip` on Windows, `.tar.gz` elsewhere. |
| 8 | Archive contents: Collabhost binary, Caddy binary, `appsettings.json`, `INSTALL.md`, `LICENSES/caddy-LICENSE`, `LICENSES/caddy-NOTICE`. |
| 9 | SHA256 per archive + aggregate `checksums.txt` (standard `sha256sum -c` format). Install scripts verify before extracting. |
| 10 | Caddy bundled with escape hatch. Pinned version. Per-platform download during workflow. Startup priority: (1) `COLLABHOST_CADDY_PATH` env var, (2) existing admin on `localhost:2019`, (3) bundled sidecar. Probe admin API with 3-5s timeout after launch; hard-fail with clear message. |
| 11 | Install mechanism: Aspire model. `install.sh` + `install.ps1`. Download-then-execute primary. Piped `curl | bash` / `iwr | iex` documented as shortcut. Location `$HOME/.collabhost/bin`, user-space. OS/arch detection, SHA256 verification, PATH integration. Merge-safe -- preserves `data/` and `appsettings.Local.json`. `dotnet tool install` and `winget` noted as v1+ follow-ups, NOT built here. |
| 12 | Install scripts hosted via GitHub Pages, source `main:/docs`. Public URL `https://mrbildo.github.io/collabhost/install.sh`. Future `docs/CNAME` for `collabhost.dev` noted but not day-1. |
| 13 | Data path default `./data/` relative to binary. Every configurable path exposes a `COLLABHOST_*_PATH` env var override. Layering system (#104) builds on top; this spec only provides the env-var floor. |
| 14 | First-run admin key logged to stdout as today (card #152). Override via `Admin:AuthKey`. INSTALL.md documents where to find it. |
| 15 | macOS Gatekeeper `xattr -d com.apple.quarantine` workaround documented day-1 in INSTALL.md inside the archive. |
| 16 | Release hosting: GitHub Releases. No CDN. No external hosting. |
| 17 | NOT in v1: Docker, MSI, .deb/.rpm, Homebrew tap, code signing, notarization. |

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
      - name: Publish
        working-directory: backend
        run: >
          dotnet publish Collabhost.Api/Collabhost.Api.csproj
          -c Release
          -r ${{ matrix.rid }}
          --self-contained
          -p:PublishSingleFile=true
          -p:Version=${{ needs.extract-version.outputs.version }}
          -o ../publish/collabhost-${{ matrix.rid }}

      # 4. Stage archive contents
      - name: Stage archive
        shell: bash
        run: |
          STAGE="publish/collabhost-${{ matrix.rid }}"
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
        run: |
          Compress-Archive -Path publish/collabhost-${{ matrix.rid }}/* -DestinationPath collabhost-${{ matrix.rid }}.zip

      - name: Archive (tar.gz)
        if: matrix.ext == 'tar.gz'
        shell: bash
        run: |
          tar -czf collabhost-${{ matrix.rid }}.tar.gz -C publish/collabhost-${{ matrix.rid }} .

      # 6. Per-archive checksum
      - name: Checksum
        shell: bash
        run: |
          ARCHIVE="collabhost-${{ matrix.rid }}.${{ matrix.ext }}"
          if command -v sha256sum >/dev/null 2>&1; then
            sha256sum "$ARCHIVE" > "$ARCHIVE.sha256"
          else
            shasum -a 256 "$ARCHIVE" > "$ARCHIVE.sha256"
          fi

      # 7. Upload archive + checksum to the release
      - name: Upload release assets
        env:
          GH_TOKEN: ${{ github.token }}
        shell: bash
        run: |
          gh release upload "${{ github.event.release.tag_name }}" \
            "collabhost-${{ matrix.rid }}.${{ matrix.ext }}" \
            "collabhost-${{ matrix.rid }}.${{ matrix.ext }}.sha256" \
            --clobber

      # 8. Upload per-leg checksum to workflow artifacts for aggregation
      - uses: actions/upload-artifact@v4
        with:
          name: checksum-${{ matrix.rid }}
          path: collabhost-${{ matrix.rid }}.${{ matrix.ext }}.sha256
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

| RID | Archive | Caddy asset | Notes |
|-----|---------|-------------|-------|
| `win-x64` | `collabhost-win-x64.zip` | `caddy_{VER}_windows_amd64.zip` | `Collabhost.Api.exe`, `caddy.exe` |
| `osx-arm64` | `collabhost-osx-arm64.tar.gz` | `caddy_{VER}_mac_arm64.tar.gz` | Apple Silicon; primary Mac target |
| `osx-x64` | `collabhost-osx-x64.tar.gz` | `caddy_{VER}_mac_amd64.tar.gz` | Intel Mac |
| `linux-x64` | `collabhost-linux-x64.tar.gz` | `caddy_{VER}_linux_amd64.tar.gz` | |
| `linux-arm64` | `collabhost-linux-arm64.tar.gz` | `caddy_{VER}_linux_arm64.tar.gz` | Raspberry Pi 4/5, ARM servers |

Archive naming convention (fixed): `collabhost-{rid}.{ext}`. No version in the filename -- the version is in the tag and the Release page. This matches Collaboard and keeps install scripts simpler (URL template does not need a version substitution for the asset filename itself, only for the Caddy source asset during build).

**Trade-off acknowledged:** Including the version in the archive filename (e.g., `collabhost-v0.1.0-linux-x64.tar.gz`) would be helpful when archives are passed around out-of-band, but it also means install scripts need to construct filenames with the version. Current decision is to omit it to match Collaboard. Revisit if operators ask.

---

## 5. Artifact Layout

### 5.1 Archive tree (platform-agnostic)

```
collabhost-<rid>/
├── Collabhost.Api           (or Collabhost.Api.exe on win-x64)  [~80 MB, self-contained single-file]
├── caddy                    (or caddy.exe on win-x64)           [~40 MB, pinned upstream build]
├── appsettings.json                                             [~500 bytes, shipped default config]
├── INSTALL.md                                                   [~2 KB]
└── LICENSES/
    ├── caddy-LICENSE                                            [~11 KB, Apache 2.0 full text]
    └── caddy-NOTICE                                             [~100 bytes]
```

### 5.2 What each file is

- **`Collabhost.Api[.exe]`** -- the API + embedded SPA + native SQLite + .NET runtime. Self-contained single-file. No external runtime requirement.
- **`caddy[.exe]`** -- the pinned Caddy binary. Upstream build from github.com/caddyserver/caddy releases. Supervised as a child process by Collabhost under the `proxy` system-service app.
- **`appsettings.json`** -- the shipped default configuration. Contains the data-path default, proxy defaults, typestore defaults. Users override via `appsettings.Local.json` sibling file or env vars (see section 12).
- **`INSTALL.md`** -- short installation + first-run guide. Ships inside the archive so manual downloaders get it without leaving the extracted directory. See section 13.
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
| `Collabhost.Api[.exe]` (self-contained single-file, no trim, no R2R) | 75-95 MB | 30-40 MB |
| `caddy[.exe]` | 40-45 MB | 18-22 MB |
| `appsettings.json` + `INSTALL.md` + `LICENSES/*` | ~15 KB | ~5 KB |
| **Total archive (.tar.gz)** | ~120-140 MB | **~50-65 MB** |
| **Total archive (.zip, win-x64)** | ~120-140 MB | **~55-70 MB** |

The .NET self-contained + single-file + native-libs-for-self-extract footprint is the dominant cost. `PublishTrimmed` would cut ~30-40%, but we're leaving it off for v1 to avoid runtime surprises on reflection-heavy code (EF Core, MCP SDK, JSON source generators). Revisit in a follow-up card once we have real operator telemetry that archive size matters.

### 5.4 What's explicitly NOT in the archive

- `appsettings.Local.json` -- never shipped. Users create it if they want config overrides.
- `appsettings.Development.json` -- dev-only, not copied.
- `data/` -- created at first run, never shipped.
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

### 6.4 Escape-hatch startup logic (the core design)

**Context check:** The current `ProxySettings.BinaryPath` defaults to `"caddy"` (bare name, resolved via PATH using a `where`/`which` subprocess in `ProxyAppSeeder.ResolveFromPath`). The current logic is single-path: if that fails, the proxy app is not seeded and the warning in `ProxyAppSeeder.SeedAsync` tells the user to install Caddy or set a custom path.

For v1 release, this resolution must be reshaped into a priority chain. There are two separate things that need changing:

1. **`ResolveBinaryPath` priority chain** -- which binary to launch.
2. **Admin-port probe** -- after launch, verify Caddy is actually alive and responding on its admin port before treating the proxy as available.

#### 6.4.1 Priority chain for binary resolution

Pseudocode for the reshaped `ProxyAppSeeder.ResolveBinaryPath` (or a new sibling method):

```
function ResolveCaddyBinary(binaryPath setting):
    // Priority 1: explicit env var override
    envOverride = Environment.GetEnvironmentVariable("COLLABHOST_CADDY_PATH")
    if envOverride is non-empty:
        if File.Exists(envOverride):
            return envOverride (absolute)
        else:
            log warning: "COLLABHOST_CADDY_PATH set to '{envOverride}' but file not found; falling back to detection"

    // Priority 2: user already running Caddy (detect via admin port)
    // NOTE: this is handled *outside* ResolveCaddyBinary -- see 6.4.2.
    // Binary resolution only covers "which binary do we launch if we need to launch one."

    // Priority 3: bundled sidecar (next to the Collabhost binary)
    bundledPath = Path.Combine(AppContext.BaseDirectory,
                               OperatingSystem.IsWindows() ? "caddy.exe" : "caddy")
    if File.Exists(bundledPath):
        return bundledPath

    // Priority 4 (explicit config escape hatch): legacy Proxy:BinaryPath setting
    // This preserves the pre-v1 behavior of letting operators point at any binary.
    if binaryPath setting is non-empty:
        return ResolveLegacyBinaryPath(binaryPath setting)

    return null
```

Rationale for ordering:
- **Env var first** so operators can override without touching config files. This is the "I know what I'm doing, here's the binary" hatch.
- **Bundled sidecar next** because it's what ships in the archive and should be the default experience. Bundled > PATH because an operator who installed a random Caddy via winget shouldn't have their Collabhost silently diverge from the pinned version.
- **Config-setting path last** preserves backward compatibility for dev setups and for operators who explicitly configure a path in `appsettings.Local.json`. This absorbs the current behavior.

**What happens to the existing `"Proxy:BinaryPath": "caddy"` default in `appsettings.json`?** Change the default to empty string (or remove the field so the config is optional). The bundled sidecar resolution takes precedence. Dev-time operators who rely on the PATH-resolved Caddy can set `Proxy:BinaryPath = "caddy"` in `appsettings.Development.json`, which already is the dev overlay. This is a small behavior change that needs a line in the release notes but no user-visible rupture for production.

#### 6.4.2 Admin-port probe (existing-Caddy detection and post-launch verification)

Two separate probes, different purposes:

**Probe A (pre-launch, existing-Caddy detection):** Before the Supervisor asks Caddy to start, check if something is already responding on `http://localhost:2019/config/`. If yes, that operator is running their own Caddy; Collabhost should *not* launch its own sidecar and should instead connect to the existing admin endpoint.

```
function IsExistingCaddyAvailable() -> (bool, int portUsed):
    using HttpClient { Timeout = 1 second }:
        try: GET http://localhost:2019/config/
        if response is 2xx: return (true, 2019)
    return (false, 0)
```

Decision 10 names port 2019 specifically. This is Caddy's documented default admin port. We do not probe the dynamically-allocated port that Collabhost assigns when it launches its *own* Caddy (that would defeat the purpose -- it's user configuration we're sensing).

**If Probe A returns true:**
- Skip the normal "launch Caddy as managed process" flow.
- Reconfigure the injected `ICaddyClient` to point at `http://localhost:2019`.
- Mark the `proxy` app as "external" (some flag in its entity or capability override) so the UI can show "Proxy: externally managed" instead of "Stopped".
- Do NOT push our own admin port into `ProxyArgumentProvider` or try to supervise a process.

This is the most invasive change to the existing subsystems in the whole release spec. Section 6.5 names the affected files.

**Probe B (post-launch, health verification):** Decision 10 requires that after launching the bundled Caddy, we probe the admin API with a 3-5s timeout and hard-fail on no response. The current `CaddyClient.IsReadyAsync` method already exists and is used elsewhere; we wrap it in a retry loop:

```
async function VerifyCaddyReady(caddyClient, logger) -> bool:
    deadline = Now + 5 seconds
    while Now < deadline:
        if await caddyClient.IsReadyAsync(5s timeout):
            return true
        await Delay(200ms)

    logger.Fatal(
        "Bundled Caddy launched but admin API did not respond within 5s " +
        "at {AdminBaseAddress}. The proxy cannot be managed. " +
        "Check logs at {CaddyLogPath} and consider running a different Caddy " +
        "via COLLABHOST_CADDY_PATH or by starting Caddy externally on port 2019."
    )
    return false
```

The "hard-fail with clear log message" requirement means: log error-level, disable the proxy subsystem for this boot, and continue running Collabhost without routing. Killing the API process entirely is too aggressive -- Collabhost's dashboard and registry are still useful without proxy, and operators can fix Caddy and restart. (This is a small softening of the "hard-fail" phrasing in decision 10. Flagged as an open question in section 17 for Bill's confirmation.)

### 6.5 Affected source files

Concrete list of files this introduces or changes:

| File | Change |
|------|--------|
| `backend/Collabhost.Api/appsettings.json` | Change `Proxy:BinaryPath` default from `"caddy"` to `""` (or remove). |
| `backend/Collabhost.Api/Proxy/ProxySettings.cs` | Relax `BinaryPath` to non-required (nullable / default empty). |
| `backend/Collabhost.Api/Proxy/ProxyAppSeeder.cs` | `ResolveBinaryPath` becomes a priority-chain method (see 6.4.1). Consider splitting into a new `CaddyResolver` class for clarity, since the chain is 4 steps and non-trivial. |
| `backend/Collabhost.Api/Proxy/CaddyResolver.cs` (NEW) | The priority-chain resolver, plus `IsExistingCaddyAvailable()`. |
| `backend/Collabhost.Api/Proxy/_Registration.cs` | At registration time, call `CaddyResolver.IsExistingCaddyAvailable()`. If true, set the `HttpClient`'s `BaseAddress` to `localhost:2019` instead of the dynamically-allocated `AdminPort`, and skip `ProxyArgumentProvider` registration. |
| `backend/Collabhost.Api/Proxy/ProxyManager.cs` | On startup, after Supervisor launches Caddy (the "we launched our own" path), await `VerifyCaddyReady` with 5s timeout. On failure, log fatal and disable route sync for this process lifetime. |
| `backend/Collabhost.Api/Program.cs` | Move the `ProxyAppSeeder.SeedAsync` call out of the `IsDevelopment()` block. Production releases need to seed too. (See section 14 "existing code review" for context.) |

**Test files that will need updates:** `ProxyAppSeederTests`, `ProxyArgumentProviderTests`, anything that currently assumes a specific `BinaryPath`. List in section 15.

---

## 7. Checksum Generation

### 7.1 Per-archive format

Each matrix leg produces `collabhost-<rid>.<ext>.sha256` with one line:

```
<64-char-hex-sha256>  collabhost-<rid>.<ext>
```

This is the exact output of `sha256sum <archive>` (Linux) or `shasum -a 256 <archive>` (macOS). The two-space separator and trailing newline are the portable-standard format understood by `sha256sum -c` and `shasum -a 256 -c`.

Each file is uploaded to the Release alongside its archive (step 7 of the matrix job). Manual downloaders can verify with:

```bash
# Download both files, then:
sha256sum -c collabhost-linux-x64.tar.gz.sha256
```

### 7.2 Aggregate `checksums.txt`

The `publish-checksums` job concatenates all per-leg sha256 files into one `checksums.txt`:

```
a1b2c3...  collabhost-linux-x64.tar.gz
d4e5f6...  collabhost-linux-arm64.tar.gz
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

```
// At top of Program.cs, before WebApplication.CreateBuilder:
if (args.Any(a => a == "--version" || a == "-v"))
{
    Console.WriteLine(Collabhost.Api.Platform.VersionInfo.Current);
    return 0;
}
```

Stdout format: just the version, no prefix. `0.1.0\n`. Scripts that want it can read it directly. (Collaboard's format is `Collaboard 1.0.0` which includes the product name. I prefer bare version for machine-consumption; product name is obvious from the binary name. Flagged in section 17 as an open question if Bill prefers Collaboard's format for consistency.)

Exit code: 0.

Edge cases:
- `--version` before any other argument works because we short-circuit at the top of Program.cs.
- Combining `--version` with other flags: we exit immediately, ignoring them. This is the POSIX convention.
- `-v` could collide with a verbose flag later. Keep `--version` as the canonical; `-v` is a convenience alias we can drop if it conflicts.

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

Asset URL template:
```
https://github.com/mrbildo/collabhost/releases/download/{tag}/collabhost-{rid}.{ext}
https://github.com/mrbildo/collabhost/releases/download/{tag}/checksums.txt
```

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
- `data/` directory (and everything inside)
- `appsettings.Local.json`

**Overwrite on re-run:**
- `Collabhost.Api` binary
- `caddy` binary
- `appsettings.json` (shipped defaults)
- `INSTALL.md`
- `LICENSES/`

**`install.sh`:**

```bash
# After verify, extract to a temp directory
TMP_EXTRACT=$(mktemp -d)
tar -xzf "$ARCHIVE" -C "$TMP_EXTRACT"

mkdir -p "$INSTALL_PATH"

# Overwrite files that are part of the bundle
cp "$TMP_EXTRACT/Collabhost.Api"    "$INSTALL_PATH/" 2>/dev/null || true
cp "$TMP_EXTRACT/caddy"             "$INSTALL_PATH/" 2>/dev/null || true
cp "$TMP_EXTRACT/appsettings.json"  "$INSTALL_PATH/"
cp "$TMP_EXTRACT/INSTALL.md"        "$INSTALL_PATH/"
mkdir -p "$INSTALL_PATH/LICENSES"
cp "$TMP_EXTRACT/LICENSES/"*        "$INSTALL_PATH/LICENSES/"

# Preserve data/ and appsettings.Local.json -- never copy them from the archive
# (they aren't in the archive anyway). Just leave them alone on disk.

chmod +x "$INSTALL_PATH/Collabhost.Api" "$INSTALL_PATH/caddy" 2>/dev/null || true
rm -rf "$TMP_EXTRACT"
```

**`install.ps1`:** same shape, with `Expand-Archive` and `Copy-Item`.

The critical property: the archive never contains `data/` or `appsettings.Local.json`, so there's nothing to accidentally overwrite. We just don't touch them on disk. Merge-safe by construction.

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
Without removing it, you will see "`Collabhost.Api` cannot be opened because the
developer cannot be verified" the first time you run it.

Because Collabhost's binaries are not notarized in v1, you need to clear the
attribute manually. From the directory where you extracted the archive:

    xattr -d com.apple.quarantine Collabhost.Api
    xattr -d com.apple.quarantine caddy

Why: Apple requires an Apple Developer Program enrollment (and a per-release
notarization step) before binaries launch cleanly. We're skipping that for v1
to avoid the $99/year enrollment friction. If Collabhost's macOS usage grows
enough to warrant it, we will notarize in a later release.

The `install.sh` script runs these `xattr` commands for you automatically.
This note is for operators who download and extract the archive manually.
```

### 11.2 Auto-clear in install.sh -- recommendation

**Proposal: yes, `install.sh` runs `xattr -d` automatically on macOS after extraction.**

Tradeoffs:

| Pro | Con |
|-----|-----|
| Matches operator expectations -- install script should produce a working install. | Mildly surprising -- clearing quarantine is a security-sensitive op. |
| Removes a footgun for users who miss the note. | An attacker who can trick a user into running a malicious install.sh could insert arbitrary commands too, so this doesn't materially worsen the threat model. |
| Aspire-style installs set up working state; this is the same. | We haven't asked Bill. |

Implementation:

```bash
if [ "$(uname -s)" = "Darwin" ]; then
  xattr -d com.apple.quarantine "$INSTALL_PATH/Collabhost.Api" 2>/dev/null || true
  xattr -d com.apple.quarantine "$INSTALL_PATH/caddy"           2>/dev/null || true
fi
```

`|| true` because `xattr -d` errors if the attribute isn't set (e.g., on a re-run).

INSTALL.md updates: "The `install.sh` script runs these commands for you automatically. This note is for operators who download and extract the archive manually."

Flagged in section 17 as something to confirm with Bill. If he prefers the scripted version only mention the manual steps, we drop the auto-clear. Zero extra work to remove.

---

## 12. Path / Data Directory Handling

### 12.1 Default data path

Today `appsettings.json` defines:

```json
"ConnectionStrings": { "Host": "Data Source=./db/collabhost.db" }
```

**Change:** default moves from `./db/collabhost.db` to `./data/collabhost.db` to match decision 13. This is a breaking change for local dev databases, but harmless because dev environments are re-seeded from migrations anyway. Document in the release notes.

Both `./db/` and `./data/` are relative to `AppContext.BaseDirectory`, which is the directory containing the binary. This is where we want it -- the install script extracts to `$HOME/.collabhost/bin`, and the SQLite file lives at `$HOME/.collabhost/bin/data/collabhost.db`.

### 12.2 Env var overrides -- the floor for #104

**Every configurable path exposes a `COLLABHOST_*_PATH` env var override.** This is the floor decision 13 asks for. #104 will build a proper layering system (env var -> file -> UI) on top; this spec guarantees the env var row exists.

The paths I identified in the codebase:

| Path | Current setting | Env var override (new) |
|------|-----------------|------------------------|
| SQLite DB path | `ConnectionStrings:Host` | `COLLABHOST_DATA_PATH` -> resolved DB inside this dir |
| User types directory | `TypeStore:UserTypesDirectory` | `COLLABHOST_USER_TYPES_PATH` |
| Tools directory | `Platform:ToolsDirectory` | `COLLABHOST_TOOLS_PATH` |
| Caddy binary | `Proxy:BinaryPath` | `COLLABHOST_CADDY_PATH` (already in decision 10) |
| Caddy bootstrap config | (written to `Path.GetTempPath()/collabhost/caddy-bootstrap.json` in `ProxyArgumentProvider`) | `COLLABHOST_TEMP_PATH` |

**Shape: env var wins over `appsettings.json`.** ASP.NET Core's configuration pipeline supports this natively via `AddEnvironmentVariables` with a prefix:

```csharp
// In Program.cs, during WebApplicationBuilder setup:
builder.Configuration.AddEnvironmentVariables(prefix: "COLLABHOST_");
```

Then the env var `COLLABHOST_Proxy__BinaryPath` becomes equivalent to the config key `Proxy:BinaryPath`. That's the standard idiom -- but it exposes the internal key names and uses ASP.NET Core's double-underscore nesting, which is awkward for operators.

**Alternative (recommended):** expose clean top-level env vars and read them explicitly in the registration code. E.g., `CaddyResolver` reads `COLLABHOST_CADDY_PATH` directly (already in the spec). `AddDataAccess` checks `COLLABHOST_DATA_PATH` and overrides the connection string. Each subsystem owns one env var lookup; the call sites are obvious; no magic key translation.

This is a nuanced call. Recommendation: **explicit top-level env vars per path** (small, obvious, documentable), not the underscore-nested mechanism. #104 can layer a proper config system on top later.

**The canonical list of env vars this release ships:**

- `COLLABHOST_DATA_PATH` (directory; SQLite lives inside)
- `COLLABHOST_USER_TYPES_PATH` (directory containing user-type JSON files)
- `COLLABHOST_TOOLS_PATH` (directory)
- `COLLABHOST_CADDY_PATH` (file path, not directory)
- `COLLABHOST_TEMP_PATH` (directory for bootstrap config scratch files)

The naming is deliberate: `_PATH` suffix even for directories, for consistency with `COLLABHOST_CADDY_PATH` (which is a file). If #104 wants to rename some of these to `_DIR` later, that's a string-substitution migration.

### 12.3 What this spec does NOT design

The full config layering stack (file -> UI -> env var precedence) is #104's job. This spec commits to:

1. Env var overrides exist for all five paths above.
2. They take precedence over `appsettings.json` (and `appsettings.Local.json`).
3. The v1 install ships with no env vars set -- defaults apply.

#104 can add UI-settable values, a formal precedence order, validation, etc. without breaking the env var floor.

---

## 13. INSTALL.md Contents Outline

Target length: ~3-4 pages (roughly 100-150 lines of Markdown). Shipped inside the archive; also visible on GitHub for the repo.

### 13.1 Section list

1. **Quick start** -- the 3 commands operators actually need (extract, run, open browser).
2. **What's in this archive** -- list of files, one-line purpose for each (mirrors section 5.2 above).
3. **First-run admin key** -- where to look in stdout, how to override via `Admin:AuthKey`.
4. **Configuration** -- the `appsettings.json` -> `appsettings.Local.json` overlay, plus the env var list from section 12.2.
5. **macOS: first-run quarantine** -- the exact text from section 11.1.
6. **Updating** -- "re-run the install script; `data/` and `appsettings.Local.json` are preserved."
7. **Troubleshooting** -- 3-4 known issues:
   - Caddy did not start (check logs, try `COLLABHOST_CADDY_PATH`).
   - Port 443 already in use (Caddy's ListenAddress).
   - `Collabhost.Api` does not run on macOS (link back to Gatekeeper section).
   - SQLite file permission errors on Linux (check `$HOME/.collabhost/bin/data/` ownership).
8. **Uninstall** -- `rm -rf $HOME/.collabhost`.
9. **Verifying checksums** -- for manual downloaders.
10. **Version & diagnostics** -- `Collabhost.Api --version`, `GET /api/v1/status`.

### 13.2 What goes where

Some content is in both INSTALL.md (in the archive) and the post-#155 README. The rule: INSTALL.md is for the already-downloaded operator (practical, concrete, local); README is for the first-time reader (conceptual, "why Collabhost exists," then links to install). Duplication is acceptable where the content is a single bullet (e.g., the install command). If it's a paragraph or more, INSTALL.md owns it and README links.

Linked from README post-#155: yes, from the "Installation" section.

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

At the top of `Program.cs`, before `WebApplication.CreateBuilder(args)`. The assembly attributes are available at that point because they live on the compiled assembly, not on the runtime host. Showed the ~6-line insertion in section 8.5.

### 14.4 Side effect on /api/v1/status

`SystemEndpoints.GetStatus` currently embeds a raw `InformationalVersion` read. Replace with `VersionInfo.Current`. The response shape doesn't change (still has a `version` field). The value becomes stripped-commit-hash clean. One test to update (`SystemEndpointsTests` or equivalent -- if one exists).

### 14.5 A small cleanup opportunity

The MCP `DiscoveryTools` reads version for `get_system_status`. The MCP `_McpRegistration` reads it for `Implementation.Version`. These are two reads of the same value, both at runtime. Neither is hot. After this refactor, both just call `VersionInfo.Current`. Won't touch caching -- `Lazy<string>` already handles it.

---

## 15. Test Strategy

Four categories: version injection, escape-hatch probe, checksum format, workflow end-to-end.

### 15.1 Unit tests (new)

**`VersionInfoTests`:**
- `Current_WithoutCommitHash_ReturnsRawValue` -- given no `+`, returns as-is.
- `Current_WithCommitHash_StripsSuffix` -- `0.1.0+abc123` -> `0.1.0`.
- `Current_EmptyAttribute_ReturnsDefault` -- handles missing attribute by returning `0.0.0`.

These are testable via reflection against a stub assembly attribute, but simplest is to extract the strip logic into a pure method (`StripCommitHash(string raw)`) and unit-test that directly. `Current` is a thin Lazy wrapper.

**`CaddyResolverTests`:**
- `Resolve_EnvVarSetAndExists_ReturnsEnvVarPath`
- `Resolve_EnvVarSetButMissingFile_LogsWarningAndFallsThrough`
- `Resolve_BundledSidecarExists_ReturnsBundledPath` -- with a temp-dir fake BaseDirectory.
- `Resolve_FallsBackToLegacyBinaryPath` -- when env var and bundle both absent.
- `Resolve_AllPathsExhausted_ReturnsNull`

**`ExistingCaddyProbeTests`:**
- `IsExistingCaddyAvailable_NothingOnPort_ReturnsFalse` -- uses a free port detected at test start.
- `IsExistingCaddyAvailable_ExistingCaddy_ReturnsTrue` -- spin up a local HTTP listener responding 200 on `/config/` for the duration of the test.
- `IsExistingCaddyAvailable_TimeoutRespected` -- listener accepts the connection but never responds. Assert the probe returns false within ~1.5s (probe timeout is 1s).

**CLI `--version` flag:**
- Integration-ish test in `Collabhost.Api.Tests`: launch the API with `args = ["--version"]`, assert stdout contains the version and exit code is 0. Because Program.cs short-circuits, the web host never starts. `ProcessStartInfo.RedirectStandardOutput = true`, read stdout, compare.

### 15.2 Integration tests (existing + updates)

Existing `ProxyAppSeederTests` need updates for the reshaped binary resolution. The three current tests (`ResolveBinaryPath_BareName_ResolvesFromPath`, etc.) still apply to the legacy path but the priority-chain above it is new.

Update list:
- `ProxyAppSeederTests` -- verify the seed still works when Caddy is available at the bundled location, env-var location, and legacy config.
- `ProxyArgumentProviderTests` -- unchanged; the argument provider is unaffected by the resolver change.
- `CaddyClientTests` -- if they exist; if not, add one that asserts `IsReadyAsync` returns true/false based on a local test HTTP listener.
- Tests that construct `ProxySettings` with `BinaryPath = "caddy"` -- now the resolution chain does not require that setting to be non-empty. Update fixtures.

### 15.3 What to verify in the CI workflow itself

- Version injection: build, then `./publish/collabhost-linux-x64/Collabhost.Api --version` -> expect `0.1.0` (or whatever the tag was).
- Escape-hatch probe: no CI test covers this directly because it requires a running Caddy. Covered by unit tests at the probe level (15.1).
- Checksum format: build archive, `sha256sum -c collabhost-linux-x64.tar.gz.sha256` should succeed.

These are scriptable as a final "self-check" step in the matrix leg if we want belt-and-suspenders:

```yaml
      - name: Self-check
        shell: bash
        run: |
          sha256sum -c collabhost-${{ matrix.rid }}.${{ matrix.ext }}.sha256

          # Version check requires extract + run; skip for RIDs that won't run on the CI host.
          if [ "${{ matrix.rid }}" = "linux-x64" ] && [ "$RUNNER_OS" = "Linux" ]; then
            mkdir -p verify
            tar -xzf collabhost-linux-x64.tar.gz -C verify
            ACTUAL=$(./verify/Collabhost.Api --version)
            EXPECTED="${{ needs.extract-version.outputs.version }}"
            if [ "$ACTUAL" != "$EXPECTED" ]; then
              echo "Version mismatch. Expected: $EXPECTED, Got: $ACTUAL" >&2
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
   - Expect 5 archives + 5 per-archive `.sha256` files + 1 `checksums.txt`.
   - Download one at random, run `sha256sum -c`, verify.
6. **Smoke test.** On a test machine, run `install.sh` or `install.ps1` from the published URL. Run the binary. Hit `/api/v1/status`. Verify the version.
7. **Close out.** Post-release, update any cards blocked on the release. (For #153 specifically, close the card.)

### 16.2 Troubleshooting

**Workflow failed mid-matrix (one leg red, four green).**
- Go to the failed leg's logs.
- Common cause: Caddy download failed (transient GitHub 5xx). Re-run the failed leg only from the Actions UI.
- `gh release upload ... --clobber` means re-running is safe -- it overwrites, doesn't duplicate.

**Artifact missing from the Release page but workflow green.**
- Unlikely but possible. Check the final step of the matrix leg's log for `gh release upload` success.
- Manual fix: `gh release upload v0.1.0 collabhost-linux-x64.tar.gz --clobber` from a local checkout.

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
| CI self-check doesn't catch a broken single-file bundle | Medium | Medium | Add the section 15.3 self-check as a workflow step. |
| Install script hosted on GitHub Pages becomes unavailable | Low | High | GitHub Pages has strong uptime. If this is ever a real worry, mirror the scripts to Cloudflare Pages as a backup. Not day-1. |
| Checksum verification fails because of download corruption | Medium | Low (user self-heals by re-running) | Install scripts exit with clear error; users re-run. |
| macOS Gatekeeper blocks binaries despite `xattr` cleanup | Low | Medium | INSTALL.md explains; install.sh auto-clears. If operators still hit it, they can dismiss via System Settings -> Privacy & Security. |
| Self-contained single-file bundle size too large | Medium | Low | Already called out in section 5.3. Not blocking v1. `PublishTrimmed` available as a follow-up optimization. |
| Pinned Caddy has a CVE before we notice | Medium | Medium-High | Card #154 owns this process. We acknowledge the exposure here and push response logic out. |
| Tag-regex gate rejects a legitimate tag someone wants to ship | Low | Low | Error message is clear, fix is a single line in `publish.yml`. |
| `Program.cs` environment move (seeding out of `IsDevelopment`) exposes migration bugs in production | Medium | Medium | Test on a clean machine before shipping v0.1.0. Migration-on-startup is not a new pattern in .NET apps; it's just new to Collabhost production. |
| `COLLABHOST_DATA_PATH` env var collides with a pre-existing user env var | Very Low | Low | Prefix `COLLABHOST_` is unique enough. |

### 17.2 Open questions (need Bill's input before implementation)

1. **Hard-fail vs soft-fail when the post-launch Caddy probe fails.** Decision 10 says "hard-fail with clear log message." Section 6.4.2 proposes interpreting this as "disable the proxy subsystem for this boot, keep the API running." Killing the API process is too aggressive because the dashboard is still useful. Want Bill's call.

2. **`--version` stdout format.** Collaboard ships `Collaboard 1.0.0`. I prefer bare `0.1.0` for machine-consumption. Flag as minor but worth confirming before implementation.

3. **Auto-clear `com.apple.quarantine` in `install.sh`.** Section 11.2 proposes yes. Small footgun reduction, not a meaningful threat-model change. Want Bill's explicit OK.

4. **Dry-run workflow (`publish-dryrun.yml`) -- same PR or follow-up?** I lean follow-up (ship the real workflow first, get a release out). Want Bill's call on prioritization.

5. **Archive filename includes version? (e.g., `collabhost-v0.1.0-linux-x64.tar.gz`)** Current decision is to omit, matching Collaboard. Small operator friction either way.

6. **Should `/api/v1/status` and `/api/v1/version` both return the same stripped version, or should `/api/v1/status` include more diagnostic info (commit hash, build date) as a nested object?** Current proposal: both return stripped. If we want diagnostics, they go in `/api/v1/status` as a new sub-field, not `/api/v1/version`.

### 17.3 Anomaly surfaced from recon

**CLAUDE.md lists card #83 as a known issue ("Caddy admin port hardcoded to 2019").** This is stale. The current code in `backend/Collabhost.Api/Proxy/_Registration.cs` line 21 already allocates the port dynamically via `PortAllocator.AllocatePort()`. The actual value flows through `ProxySettings.AdminPort` and into `ProxyArgumentProvider` via the bootstrap config. Port 2019 only appears in this spec as the conventional Caddy default that we probe for *external* Caddy instances (escape hatch, decision 10). **Recommend closing card #83 as already-fixed and updating CLAUDE.md's "Known Issues" section in a trivial follow-up.**

---

## 18. Implementation Plan

Phased to keep review and merge units manageable. Each phase is a candidate branch/card. Review gates noted between phases.

### Phase 1: Version helper + CLI flag + endpoint

**Branch:** `feature/153-01-version`
**Scope:**
- `Platform/VersionInfo.cs` (new).
- Update three existing call sites (`SystemEndpoints`, `DiscoveryTools`, `_McpRegistration`).
- `Program.cs` short-circuit for `--version` / `-v`.
- `GET /api/v1/version` endpoint.
- Unit tests for `VersionInfo`.
- Integration test for `--version` flag.

**Why first:** smallest change, unblocks the workflow's `/p:Version=` step, verifiable in isolation. Gives us something immediately useful even if the rest of the release stack slips.

**Review gate:** Marcus or Dana reviews the helper shape and the endpoint contract.

### Phase 2: Escape-hatch binary resolution + probe

**Branch:** `feature/153-02-caddy-escape-hatch`
**Scope:**
- `Proxy/CaddyResolver.cs` (new, extracted from `ProxyAppSeeder.ResolveBinaryPath`).
- `Proxy/_Registration.cs` updates for existing-Caddy detection (probe A).
- `Proxy/ProxyManager.cs` updates for post-launch probe (probe B).
- `ProxyAppSeeder` refactor to use `CaddyResolver`.
- Update `appsettings.json` to remove the `Proxy:BinaryPath = "caddy"` default.
- Move `ProxyAppSeeder.SeedAsync` call out of `IsDevelopment()` in `Program.cs`.
- Env var: `COLLABHOST_CADDY_PATH`.
- Unit tests for the resolver priority chain.

**Why second:** structural change to a subsystem. Keeping it separate from the release workflow PR means the PR is a clean subsystem refactor, not a mix of application code and CI.

**Review gate:** full architectural review -- Marcus. The priority chain is the most invasive piece of the spec.

### Phase 3: Data path + env var floor

**Branch:** `feature/153-03-env-var-floor`
**Scope:**
- Add `COLLABHOST_DATA_PATH`, `COLLABHOST_USER_TYPES_PATH`, `COLLABHOST_TOOLS_PATH`, `COLLABHOST_TEMP_PATH` lookups.
- Move default SQLite path from `./db/collabhost.db` to `./data/collabhost.db`.
- Update `ProxyArgumentProvider` to honor `COLLABHOST_TEMP_PATH`.
- Update relevant registration code.

**Why third:** touches the data access, type store, and tools directory. Standalone.

**Review gate:** small footprint, Kai review is enough.

### Phase 4: Release workflow + Caddy bundle + checksums

**Branch:** `feature/153-04-publish-workflow`
**Scope:**
- `.github/workflows/publish.yml` (new).
- `caddy.version` file at repo root.
- `release-assets/INSTALL.md`, `release-assets/caddy-LICENSE`, `release-assets/caddy-NOTICE`.
- Cut `v0.1.0-dryrun.0` against a test branch if we want E2E validation (Option B in 15.4), OR wait until we're confident.

**Why fourth:** depends on phases 1-3 being merged (the workflow exercises the full stack).

**Review gate:** Marcus for architecture, Dana for any user-facing language (INSTALL.md, installer output).

### Phase 5: Install scripts + GitHub Pages

**Branch:** `feature/153-05-install-scripts`
**Scope:**
- `docs/install.sh`, `docs/install.ps1`, `docs/index.html`.
- Enable GitHub Pages in repo settings (manual op, not a PR).
- README pointer to install commands (coordination with #155).

**Why last:** depends on phase 4 producing the release artifacts it references. Install scripts are meaningless without a release to download from.

**Review gate:** Dana for the landing page, Marcus for the script logic. Both scripts should be reviewed together since they must stay in sync.

### Post-merge -- v0.1.0 release

After all five phases merge:
1. Cut the `v0.1.0` tag and release. (Section 16.1.)
2. Smoke-test the install flow on all three OSes.
3. Close card #153.
4. Notify anyone blocked on a working release pipeline (card #154 planning, card #155 README pass).

### Optional follow-up cards

- `publish-dryrun.yml` workflow (section 15.4).
- Add Caddy download checksum verification inside the workflow.
- `dotnet tool install -g collabhost` NuGet publishing (decision 11 notes as v1+).
- `winget` manifest submission (decision 11 notes as v1+).
- Homebrew custom tap (out of v1 per decision 17).

---

## Appendix A: One-page summary for review

- **Workflow:** `publish.yml` on `release: [published]`. Jobs: `extract-version` -> `build-frontend` -> `build-matrix (5 RIDs, native runners)` -> `publish-checksums`.
- **Caddy:** pinned in `caddy.version` (recommend `2.11.2`). Downloaded per-platform in-workflow. Shipped in-archive with `caddy-LICENSE` + `caddy-NOTICE`.
- **Archive:** `collabhost-<rid>.(zip|tar.gz)`. Contents: binary + `caddy` + `appsettings.json` + `INSTALL.md` + `LICENSES/`.
- **Checksums:** per-archive `.sha256` + aggregated `checksums.txt`. Install scripts verify before extracting.
- **Version:** `v{semver}` tag -> `/p:Version=` -> one static helper (`Platform/VersionInfo`) -> `GET /api/v1/version` + `--version` CLI flag.
- **Escape hatch:** `COLLABHOST_CADDY_PATH` > existing admin on `localhost:2019` > bundled sidecar > legacy config setting. Post-launch probe with 5s timeout; soft-fail (proxy disabled, API keeps running).
- **Install scripts:** `docs/install.sh` + `docs/install.ps1`. Aspire model. `$HOME/.collabhost/bin`. Merge-safe. Hosted on GitHub Pages.
- **Env var floor for #104:** `COLLABHOST_DATA_PATH`, `COLLABHOST_USER_TYPES_PATH`, `COLLABHOST_TOOLS_PATH`, `COLLABHOST_CADDY_PATH`, `COLLABHOST_TEMP_PATH`.
- **macOS:** Gatekeeper `xattr` cleanup documented in INSTALL.md; install.sh auto-clears (proposed).
- **Anomaly:** Card #83 is stale -- admin port is already dynamically allocated. Close it.
- **Phased implementation:** 5 branches, most-to-least isolatable. Version helper first, workflow fourth, scripts last.

---

*End of spec. Length: ~1100 lines. Ready for Marcus and Dana review before implementation.*
