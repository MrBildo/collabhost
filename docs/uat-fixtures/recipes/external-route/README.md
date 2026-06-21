# `external-route` UAT fixture recipe

This directory holds the recipe(s) for building the `external-route` fixture(s) used by the release UAT pass — specifically the fixture-requirements list and the `external-route` lifecycle walk.

## Build

```bash
# Linux / macOS / WSL
./build.sh

# Windows PowerShell
.\build.ps1
```

The recipe writes the side-process working directory only — it does NOT launch the side-process. The operator launches `python3 -m http.server 11235` from the built directory explicitly before registration (see the "Cross-OS" section below for the exact launcher syntax per OS).

Unlike the other recipes in this directory, `external-route` does **not** produce an artifact directory the operator points Collabhost at. Collabhost's `external-target` capability points at a `host:port` on the test box — the fixture's job is to **be that host:port**. The recipe stands up a side-process the operator launches before registration and tears down after teardown.

Build output lands at `docs/uat-fixtures/build/external-route/` (gitignored). The "build output" is the working directory the side-process runs from, not anything Collabhost reads.

## Fixtures the recipe must produce

| Fixture name | Shape | Purpose |
|---|---|---|
| `localhost-http/` | A directory containing `index.html` + a `health` file (literally — a file named `health`, not `health.html`, so `python -m http.server`'s default file-serving returns 200 on `GET /health`). The operator runs `python -m http.server 11235` from this directory before registration. | Drives the happy-path `external-route` registration walk: registers with `host: localhost`, `port: 11235`, `scheme: http`; the health-check probe at the default `/health` endpoint returns 200; routing dials `localhost:11235` and serves the directory listing through Caddy. |
| `localhost-https/` *(optional, exercise once when a TLS upstream is convenient)* | A directory with `index.html` + `health` + a self-signed cert. The operator runs an `https`-speaking server on `:11236` (e.g. `python -m http.server` with an SSL wrapper, or any tiny TLS-speaking server). | Drives the `scheme: https` code path: the emitted Caddy config carries the `transport` block with `tls: {}`; Caddy speaks TLS to the upstream while still terminating public TLS itself. |

## Registration shape the runbook points at

When the runbook says "register an `external-route` with the localhost-http fixture," the operator types:

- **Step 1, type picker** → 6th tile (`External Route`).
- **Step 2, registration form** →
  - `name` / `displayName`: any unique slug (suggested: `uat-external-route`).
  - `external-target.host`: `localhost`
  - `external-target.port`: `11235`
  - `external-target.scheme`: `http` (the form default).
- Submit. Expect 200 + redirect to `/apps/<slug>`. The route is **auto-enabled at registration** — the row reads `running` immediately, no separate Start step is needed.

## Recipe constraints

- The side-process MUST be launched **before** registration. Registering against a port nothing is listening on is a valid UAT shape too (covers the `Unreachable` health-status case) but it's a distinct assertion — the happy-path requires a live upstream.
- The `health` file (no extension) MUST exist in the directory the side-process serves from. `python -m http.server` serves files by literal name; `GET /health` returns the `health` file's contents with `Content-Type: application/octet-stream` and HTTP 200. The body content does not matter; the 200 status is what the health-check capability reads.
- Port `11235` is suggested for the happy-path fixture (uncommon enough to avoid most port-collisions on a fresh UAT host). If the port is in use on a specific test box, the operator picks an alternative — the registration form is the source of truth.
- The recipe MUST NOT depend on Docker. The whole point of the Python `http.server` choice is "no container runtime required on the UAT host." A future recipe can add a Docker-Compose-based fixture for richer testing, but the baseline runbook recipe stays Docker-free.

## Cross-OS

The fixture is **shape-only** — the only cross-OS consideration is the Python launcher syntax:

| OS | Launcher |
|---|---|
| Linux / WSL2 | `cd <fixture-dir> && python3 -m http.server 11235` (tmux pane, runs in foreground) |
| Windows | `cd <fixture-dir>; python -m http.server 11235` (PowerShell window, runs in foreground) |

The recipe scripts (`build.sh` / `build.ps1`, when implemented) write the `index.html` + `health` files into `docs/uat-fixtures/build/external-route/localhost-http/` and emit a one-line "ready to launch with: ..." message. The recipe does NOT launch the side-process — the runbook step does that explicitly so the operator sees stdout.

## Teardown

After §8 teardown of the Collabhost install, **also** stop the side-process running on the fixture port. Otherwise the next UAT run's port-allocation can race against the still-listening fixture process (the `external-route` slot itself is fine — the fixture is bound on `:11235`, not on a Collabhost-allocated port — but the operator's mental model should be "the fixture is also a thing to clean up").

If a recipe-side script writes anything outside the build directory (a temp cert, a log file), document it in the per-fixture recipe and clean it in teardown.

## What the fixture exercises

The fixture intentionally drives the load-bearing surfaces of the `external-route` app type:

- the `external-target` capability (host / port / scheme),
- the proxy config emitted for an external upstream (the `reverse_proxy` dial to the declared `host:port`, plus the `transport` TLS block on the `https` path),
- the health-check probe against an external target, and
- the per-type detail-view affordances (no logs tab, lifecycle limited to enable/disable route).
