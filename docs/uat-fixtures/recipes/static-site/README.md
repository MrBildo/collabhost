# `static-site` UAT fixture recipe

This directory holds the recipe(s) for building the `static-site` UAT fixture(s) consumed by `docs/release-uat.md` § 2 and § 4. The recipes themselves are **not yet implemented** — this README describes what they must produce. Building them out is tracked as a follow-up card.

Build output lands at `docs/uat-fixtures/build/static-site/` (gitignored).

## Fixtures the recipe must produce

| Fixture name | Shape | Purpose |
|---|---|---|
| `basic/` | A directory containing `index.html` at the root + at least one CSS file (e.g. `styles.css`) + at least one image asset (e.g. `logo.svg` or `favicon.png`). | Drives the happy-path `static-site` registration walk: detect-strategy returns `NotApplicable` (fitness `FullMatch`); routing uses Caddy `file_server` mode; probe `StaticSiteData.HasIndexHtml=true`, `HtmlFileCount=1`, `TotalAssetBytes` non-zero. |
| `with-config-json/` | The `basic/` fixture PLUS a `config.json` at the root. | Drives the `runtime-config-file` capability assertion: when the operator sets non-empty values on the `runtime-config-file` capability via the settings page, `RuntimeConfigFileWriter` materializes `<artifactDir>/config.json` with the resolved values post-start (#336). |
| `spa-bundle/` | A directory containing `index.html` + a subdirectory of static assets + the runtime expectation that any 404 falls back to `index.html` (the SPA case). | Drives the `routing.spaFallback = true` assertion: a deep-link request returns the SPA shell, not a 404. |

## Registration shape the runbook points at

When the runbook says "register a `static-site` with the basic fixture," the operator types:

- **Artifact location** → absolute path to `docs/uat-fixtures/build/static-site/basic/`
- **Serve mode** → the form pre-populates `FileServer` (the static-site default).
- **SPA fallback** → off by default; toggled on for the `spa-bundle/` fixture.

## Recipe constraints

- The `index.html` MUST have a `<!DOCTYPE html` declaration as the literal first 14 characters (the runbook § 0 SPA-shell assertion greps for this in browser-verify output).
- Asset paths in `index.html` MUST be relative — `href="./styles.css"`, `src="./logo.svg"` — so the fixture works regardless of the resolved domain.
- `config.json` (in the `with-config-json/` fixture) MUST be valid JSON with at least one key the operator could set via the `runtime-config-file` capability.
- Recipe scripts MUST be idempotent.

## Cross-OS

`static-site` fixtures are pure data — no compilation, no platform-specific behavior. One recipe per fixture, used on both Windows and Linux. A single `build.sh` / `build.ps1` (or just committed asset files in the recipe directory itself) suffices.
