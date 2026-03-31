# Spec: App Type Architecture — Capability Composition Model

**Status:** Ready for implementation
**Card:** #36 on `collabhost`
**Author:** Claude + User
**Date:** 2026-03-30
**Sign-off:** 2026-03-30 (user approved after full spec discussion)

---

## Goal

Replace the current hardcoded app type system with a capability composition model. Each app type is defined by a collection of well-known capabilities with typed JSON configuration. The platform consumes capabilities generically — no per-type code branches.

---

## Design Principles

These principles are non-negotiable. Every implementation decision must align with them.

1. **No MVP shortcuts.** We are designing a small-scale, feature-rich, robust hobby PaaS. No "good enough for now."
2. **No backwards compatibility.** No migration shims, no fallbacks, no renamed `_unused` variables. Everything is on the table. The database migration is destructive (drop and recreate).
3. **Standards-aligned.** Follow 12-Factor App principles. Apps found on GitHub should host without friction.
4. **Data-driven.** Type behavior is metadata, not code. Adding a type means adding data rows, not changing C# handler logic.
5. **Capabilities are the atomic unit.** Each capability maps to exactly one frontend widget and is consumed by exactly one backend subsystem. The capability boundary is the contract between backend and frontend.
6. **Core systems vs. Bridge vs. UI.** The platform has three layers with strict boundaries:
   - **Core systems** (ProcessSupervisor, ProxyConfigManager, CapabilityResolver) — tight, efficient, concerned only with their own domain. They manage in-memory state. They do NOT shape their interfaces for UI consumption. They do NOT persist UI-friendly flags on entities.
   - **Bridge** (API endpoints) — aggregates state from multiple core systems, derives statuses, and produces response shapes the UI can consume. The bridge is the ONLY layer that combines cross-system state. All derivation logic lives here.
   - **UI** (frontend) — consumes bridge output. Never talks to core systems directly.

   This separation is critical. Core systems must remain clean and optimized. If the UI needs a derived status, the bridge computes it — the core system does not store it.

---

## Data Model

### Entity: AppType

The named type. Replaces the current simple lookup table with a richer entity.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | Guid | PK | Hardcoded in IdentifierCatalog for built-in types |
| ExternalId | string | Unique, indexed | ULID, set on creation |
| Name | string | Unique, max 50 | Slug format (lowercase, hyphens). e.g., `dotnet-app`, `react-app` |
| DisplayName | string | Required, max 100 | Human-readable. e.g., "ASP.NET Core", "React App" |
| Description | string? | Max 500 | Optional description shown in UI type picker |
| IsBuiltIn | bool | Required, default false | True for system-seeded types. UI may prevent deletion of built-in types. |

**Business rules:**
- Name is immutable after creation. It is used as a slug and may be referenced in logs, config, etc.
- DisplayName is mutable.
- Deleting an AppType is forbidden if any App references it. The API must return 409 Conflict with a message naming the apps that use it.
- Operator-created types have IsBuiltIn = false. Built-in types seeded at startup have IsBuiltIn = true.
- Built-in types CAN be modified (DisplayName, Description) but CANNOT be deleted.

### Entity: Capability

The catalog of well-known capabilities the platform understands. System-defined only — operators cannot create capabilities.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | Guid | PK | Hardcoded in IdentifierCatalog |
| Slug | string | Unique, max 50 | Kebab-case identifier. e.g., `port-injection`, `health-check` |
| DisplayName | string | Required, max 100 | Human-readable. e.g., "Port Injection", "Health Check" |
| Description | string? | Max 500 | Explains what this capability governs |
| Category | string | Required, max 20 | Either `"behavioral"` or `"informational"` |

**Business rules:**
- Capabilities are ONLY created via seed data at startup. There is no CRUD API for capabilities.
- The Slug is used in API responses (frontend reads it to select the matching widget). The Slug MUST NOT be used in backend C# code for lookups — use the IdentifierCatalog Guid instead.
- The Category field tells the frontend how to group and display capabilities. Behavioral capabilities appear in the "Configuration" section of the app detail view. Informational capabilities appear in an "About" or "Runtime Info" section.
- Deleting a capability is not supported. Capabilities are permanent once seeded.

### Entity: AppTypeCapability

The composition join. Defines which capabilities a type has AND the type-level default configuration for each.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | Guid | PK | Auto-generated |
| AppTypeId | Guid | FK → AppType, required | |
| CapabilityId | Guid | FK → Capability, required | |
| Configuration | string | Required, JSON | Type-level defaults. Must be valid JSON that deserializes to the capability's C# config type. |

**Constraints:**
- Unique index on (AppTypeId, CapabilityId). A type cannot have the same capability twice.
- Cascade delete: if an AppType is deleted, its AppTypeCapability rows are deleted.

**Business rules:**
- When a new built-in AppType is seeded, its AppTypeCapability rows are seeded in the same migration/seed step.
- When an operator creates a custom AppType via the API, they provide the list of capabilities and default configurations. The API creates the AppTypeCapability rows.
- The Configuration JSON MUST be validated against the capability's C# config type at write time. If deserialization fails, the API returns 400 with a descriptive error.
- An AppType with zero AppTypeCapability rows is valid (though not useful). This is not an error condition.

### Entity: CapabilityConfiguration

Per-app instance overrides. Optional — absence means "use type defaults entirely."

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | Guid | PK | Auto-generated |
| AppId | Guid | FK → App, required | |
| AppTypeCapabilityId | Guid | FK → AppTypeCapability, required | |
| Configuration | string | Required, JSON | Operator overrides. Only contains fields the operator explicitly changed. |

**Constraints:**
- Unique index on (AppId, AppTypeCapabilityId). An app can only override a given capability once.
- Cascade delete: if an App is deleted, its CapabilityConfiguration rows are deleted.

**Business rules:**
- A CapabilityConfiguration row is ONLY created when the operator explicitly sets an override. The absence of a row means "use type defaults."
- The Configuration JSON contains ONLY the fields the operator changed, not a full copy. Example: if the type default health-check is `{ "endpoint": "/health", "intervalSeconds": 30, "timeoutSeconds": 5 }` and the operator changes only the endpoint, the override JSON is `{ "endpoint": "/api/health" }`.
- When an operator resets a capability override to defaults, the CapabilityConfiguration row is DELETED, not set to empty JSON.
- An operator can only create overrides for capabilities that their app's type actually has. If the app's type does not have the `health-check` capability, the operator cannot create a health-check override. The API must return 400 if attempted.

### Entity: App (revised)

The App entity is stripped of type-specific fields. All configuration lives in capabilities.

| Field | Type | Constraints | Notes |
|-------|------|-------------|-------|
| Id | Guid | PK | |
| ExternalId | string | Unique, indexed | ULID |
| Name | string | Unique, max 50 | Slug (lowercase, used in domain URL) |
| DisplayName | string | Required, max 100 | |
| AppTypeId | Guid | FK → AppType, required | |
| RegisteredAt | DateTime | Required | Set on creation, UTC |

**Fields removed from current App entity (moved to capabilities or runtime state):**
- `Command` → `process` capability
- `Arguments` → `process` capability
- `WorkingDirectory` → `process` capability
- `Port` → runtime state (assigned by PortAllocator at process start time, stored in-memory on ManagedProcess)
- `InstallDirectory` → `artifact` capability (future card)
- `HealthEndpoint` → `health-check` capability
- `UpdateCommand` → removed entirely (future work)
- `AutoStart` → `auto-start` capability
- `RestartPolicyId` → `restart` capability

**Business rules:**
- **Port is NOT persisted.** PortAllocator assigns a free port at process start time. The port lives in-memory on ManagedProcess. Each start may assign a different port. No DB storage.
- **InstallDirectory is NOT on the App entity.** It moves to the `artifact` capability. For now, the `artifact` capability is a future card — existing code uses TODOs/placeholders.
- If an app's type changes (if we allow that — see below), all existing CapabilityConfiguration overrides are deleted. The app starts fresh with the new type's defaults. This is destructive by design.
- Name is immutable after creation (used in domain URLs, log paths, deploy directory names).
- **No persisted state flags.** The App entity does NOT store running/stopped state. All operational state is in-memory, managed by core systems (ProcessSupervisor, ProxyConfigManager). See "Universal Stop/Start" section.

### Removed Entities

The following entities are removed entirely:

- **RestartPolicy** (lookup table) — absorbed into `restart` capability configuration
- **EnvironmentVariable** (child table of App) — absorbed into `environment-defaults` capability configuration
- **AppTypeBehavior** (C# class) — replaced by capability queries

The `IdentifierCatalog` entries for RestartPolicy and the `StringCatalog` entries for RestartPolicy display names are removed.

---

## Configuration Resolution

### The Merge Service

A single service (`ICapabilityResolver` or similar) resolves the effective configuration for any app + capability combination. ALL subsystems call this service — no subsystem reads AppTypeCapability or CapabilityConfiguration directly.

### Merge Algorithm

For a given App and Capability:

1. Find the App's AppType.
2. Find the AppTypeCapability row for that (AppType, Capability) pair.
3. If no AppTypeCapability exists → the app's type does not have this capability. Return null. The calling subsystem must handle null (meaning "this app does not have this capability").
4. Read the type-level Configuration JSON from AppTypeCapability.
5. Find the CapabilityConfiguration row for (App, AppTypeCapability).
6. If no CapabilityConfiguration exists → return the type-level defaults as-is (deserialized to the C# config type).
7. If a CapabilityConfiguration exists → JSON-merge the override on top of the type defaults. Override values win per field. Then deserialize the merged JSON to the C# config type.

### JSON Merge Rules

The merge is a shallow merge at the top level of the JSON object. For each key in the override JSON, the override value replaces the type default value entirely.

**Exception for dictionary fields:** The `environment-defaults` capability has a `defaults` field that is a dictionary (key-value pairs). For this field, the merge is per-key within the dictionary: override keys replace or add to the type default keys. Keys present in the type defaults but absent in the override are preserved.

Example:
```
Type defaults:   { "defaults": { "A": "1", "B": "2" } }
Override:        { "defaults": { "B": "999", "C": "3" } }
Resolved:        { "defaults": { "A": "1", "B": "999", "C": "3" } }
```

**No deep merge beyond one level.** If a field contains a nested object (other than the dictionary exception), the override replaces the entire nested object.

### C# Configuration Types

Each capability has a strongly-typed C# class. The merge service deserializes the merged JSON into the appropriate type. If deserialization fails, the merge service throws a descriptive exception — this is a data integrity error that must not be swallowed.

```csharp
// Behavioral capabilities
ProcessConfiguration { DiscoveryStrategy (string), GracefulShutdown (bool), ShutdownTimeoutSeconds (int) }
PortInjectionConfiguration { EnvironmentVariableName (string), PortFormat (string) }
RoutingConfiguration { DomainPattern (string), ServeMode (string), SpaFallback (bool?) }
HealthCheckConfiguration { Endpoint (string), IntervalSeconds (int), TimeoutSeconds (int), Retries (int?) }
EnvironmentDefaultsConfiguration { Defaults (Dictionary<string, string>) }
RestartConfiguration { Policy (string) }
AutoStartConfiguration { Enabled (bool) }

// Informational capabilities
AspNetRuntimeConfiguration { TargetFramework (string), RuntimeVersion (string), SelfContained (bool) }
NodeRuntimeConfiguration { NodeVersion (string), PackageManager (string), BuildCommand (string?) }
ReactRuntimeConfiguration { Version (string), Router (string?), Bundler (string?) }
```

**Business rules for configuration types:**
- All fields with non-nullable types are required. If the merged JSON is missing a required field, deserialization must fail.
- The `Policy` field in RestartConfiguration accepts exactly three values: `"never"`, `"onCrash"`, `"always"`. Any other value must be rejected at write time.
- The `ServeMode` field in RoutingConfiguration accepts exactly two values: `"reverseProxy"`, `"fileServer"`. Any other value must be rejected at write time.
- The `DiscoveryStrategy` field in ProcessConfiguration accepts exactly three values: `"dotnet-runtimeconfig"`, `"package-json"`, `"manual"`. Any other value must be rejected at write time.
- The `PortFormat` field in PortInjectionConfiguration must contain the placeholder `{port}`. Validation must reject formats without it.
- The `DomainPattern` field in RoutingConfiguration must contain the placeholder `{slug}`. Validation must reject patterns without it.
- The `SpaFallback` field in RoutingConfiguration is only meaningful when ServeMode is `"fileServer"`. It is ignored for `"reverseProxy"` mode. No validation error — just ignored.

---

## Discovery Strategies

When ProcessSupervisor starts an app with a `process` capability, it uses the configured `DiscoveryStrategy` to determine the command to execute.

### Strategy: `dotnet-runtimeconfig`

1. Scan the app's InstallDirectory for files matching `*.runtimeconfig.json`.
2. If exactly one is found: derive the DLL name by replacing `.runtimeconfig.json` with `.dll`. The command is `dotnet <dll-name>` with the working directory set to InstallDirectory.
3. If zero are found: fail with a clear error. Log: "No .runtimeconfig.json found in {InstallDirectory}. Cannot determine .NET entry point."
4. If more than one is found: fail with a clear error. Log: "Multiple .runtimeconfig.json files found in {InstallDirectory}: {list}. Cannot determine .NET entry point. Use the 'manual' discovery strategy and specify the command explicitly."

### Strategy: `package-json`

1. Look for `package.json` in the app's InstallDirectory.
2. If found: read the `scripts.start` field. The command is `npm start` with the working directory set to InstallDirectory.
3. If `package.json` is missing: fail with a clear error.
4. If `scripts.start` is missing from `package.json`: fail with a clear error.

### Strategy: `manual`

1. Read the `Command` and `Args` fields from the ProcessConfiguration.
2. Both are required for the `manual` strategy. If either is missing, fail with a clear error.
3. Working directory defaults to InstallDirectory if not specified.

**Manual strategy configuration (additional fields):**
```csharp
ProcessConfiguration {
    DiscoveryStrategy (string),
    GracefulShutdown (bool),
    ShutdownTimeoutSeconds (int),
    Command (string?),      // required only for "manual" strategy
    Args (string?),         // required only for "manual" strategy
    WorkingDirectory (string?)  // optional, defaults to App.InstallDirectory
}
```

**Business rules:**
- Discovery runs at process start time, not at app creation time. This means the DLL/package.json can be deployed after the app is registered.
- If discovery fails, the app transitions to an error state. The error message is stored and visible in the dashboard and logs.
- ProcessSupervisor must NOT cache discovery results. Each start runs discovery fresh (the artifacts may have changed after an update).

---

## Universal Stop/Start

All app types support Stop and Start. The operator experience is identical regardless of the app's type composition.

### State Is Derived, Not Persisted

**There is no `IsStopped` flag on the App entity.** An app's running/stopped state is derived from the actual in-memory state of its core systems at query time:

- **ProcessSupervisor** knows whether a process is alive, starting, crashed, etc.
- **ProxyConfigManager** knows whether a route is active or disabled (serving 503).

The **bridge layer** (API endpoints) queries these systems and derives a unified status for the UI. This avoids sync issues between a persisted flag and actual system state.

**What "Running" means varies by app composition:**
- .NET App: process is alive AND route is active
- Static Site: route is active
- The bridge derives the unified status by asking each relevant system for its state

**What "Stopped" means varies by app composition:**
- .NET App: process is not running AND route is serving 503
- Static Site: route is serving 503

The exact status values and presentation (unified "Running" vs. separate process/route indicators) are a bridge/UI design decision. Core systems do not define or store UI-facing statuses.

### Stop Behavior

When the operator stops an app, the bridge orchestrates across systems:

1. If the app has the `process` capability AND a process is currently running:
   a. If `GracefulShutdown` is true: send Ctrl+C (Windows) or SIGTERM (Linux) to the process.
   b. Wait up to `ShutdownTimeoutSeconds` for the process to exit.
   c. If the process has not exited after the timeout: hard-kill (Process.Kill).
   d. If `GracefulShutdown` is false: hard-kill immediately.
2. If the app has the `routing` capability: configure Caddy to return 503 Service Unavailable for the app's domain. The 503 response should include a simple body: `{"error": "Service is stopped"}` with `Content-Type: application/json`. This replaces the existing route (reverse_proxy or file_server) — it does not remove the route entirely.

### Start Behavior

When the operator starts an app:

1. If the app has the `routing` capability: reconfigure Caddy to restore the app's normal route (reverse_proxy or file_server based on the routing capability).
2. If the app has the `process` capability: start the process using the normal startup flow (discovery strategy, port injection, env var injection).
3. If the app does NOT have the `process` capability (e.g., Static Site): step 1 is sufficient — the route is active and Caddy serves files.

### Interaction with Auto-Start

When Collabhost starts up:

1. Load all apps from the database.
2. For each app with the `auto-start` capability where `Enabled` is true: start the process (if process capability exists) and configure the normal Caddy route.
3. For each app without auto-start (or `Enabled` is false): configure the normal Caddy route but do NOT start the process. The operator must start it manually.
4. Non-process apps with the `routing` capability always get their Caddy route configured at startup.

**Business rule:** Stop is an operational action ("stop it right now"). Auto-start is a configuration ("should this start when the platform starts?"). They are independent concerns. Stopping an app does NOT change its auto-start configuration. When Collabhost restarts, it follows auto-start configuration — it does not remember or care whether an app was previously stopped.

### Interaction with Restart

If an app has the `restart` capability with policy `"always"` or `"onCrash"`, crash recovery applies based on the ProcessSupervisor's in-memory state. When an operator stops an app via the Stop action, the ProcessSupervisor transitions the process to a "stopped by operator" state and does NOT apply crash recovery. This is in-memory state, not a persisted flag.

### Force Kill

The dashboard must provide a "Force Kill" action for process-based apps. This is separate from Stop. Force Kill immediately hard-kills the process without graceful shutdown, regardless of the `GracefulShutdown` setting. It does NOT stop the app — it is an emergency action for a hung process.

After a Force Kill, if the process was running (not operator-stopped) and has a restart policy, the restart policy kicks in and restarts the process.

### Core System Boundaries

**Critical for implementation agents:** The Stop/Start orchestration logic does NOT live in ProcessSupervisor or ProxyConfigManager. Those are core systems that manage their own domain:

- **ProcessSupervisor** exposes: `StartProcess()`, `StopProcess()`, `KillProcess()`, `GetProcess()` — it manages processes and their in-memory state. It does not know about routes.
- **ProxyConfigManager** exposes: `EnableRoute()`, `DisableRoute()` (serves 503) — it manages Caddy routes. It does not know about processes.
- **The bridge** (API endpoint handlers) orchestrates across both systems when the operator clicks Stop or Start. The bridge knows "stopping a .NET app means stop the process AND disable the route."

Do NOT add cross-system awareness to core systems. The bridge handles composition.

---

## Port Allocation

### When Ports Are Assigned

Ports are assigned at **process start time**, not at app creation time. They are ephemeral — each start may assign a different port. Ports are NOT persisted in the database.

1. When ProcessSupervisor starts a process, it checks if the app's type has the `port-injection` capability.
2. If yes: call PortAllocator to find a free TCP port. Store the port in-memory on the `ManagedProcess`.
3. If no: no port allocation occurs.

### Port Injection at Process Start

When ProcessSupervisor starts an app with `port-injection` capability:

1. Resolve the `port-injection` capability configuration (via merge service).
2. If the capability is not present (null): do not inject any port env var.
3. If present: read `EnvironmentVariableName` and `PortFormat` from the configuration. Replace `{port}` in the format string with the allocated port value. Inject as an environment variable.

Example for .NET:
- Configuration: `{ "environmentVariableName": "ASPNETCORE_URLS", "portFormat": "http://localhost:{port}" }`
- Allocated port: 52341
- Injected env var: `ASPNETCORE_URLS=http://localhost:52341`

Example for Node:
- Configuration: `{ "environmentVariableName": "PORT", "portFormat": "{port}" }`
- Allocated port: 52342
- Injected env var: `PORT=52342`

### Caddy Route Coordination

After the port is allocated and the process starts, ProcessSupervisor notifies ProxyConfigManager (via the existing event bus) of the assigned port. ProxyConfigManager updates the Caddy route to forward traffic to the new port. This happens on every start — ports are not stable across restarts.

---

## Environment Variable Injection

When ProcessSupervisor starts an app, environment variables come from two capability sources, applied in this order:

1. **Resolved `environment-defaults` configuration** — the merged type defaults + operator overrides dictionary. All key-value pairs are injected as env vars.
2. **Resolved `port-injection` configuration** — the port env var is injected (as described above).

**Precedence rule:** If `environment-defaults` and `port-injection` both set the same env var name, `port-injection` wins. The platform-assigned port always takes precedence over a manually configured env var with the same name. This prevents an operator from accidentally overriding the port binding.

**No other sources:** There is no separate EnvironmentVariable table. All env vars come from capabilities. If the operator needs a custom env var (e.g., a connection string), they add it via the `environment-defaults` capability override.

---

## Caddy Route Configuration

### Route Generation

The ProxyConfigManager reads the `routing` capability for each app and generates the appropriate Caddy route.

**For `serveMode: "reverseProxy"`:**
- Route matches the domain (derived from `DomainPattern` with `{slug}` replaced by `App.Name`).
- Handler: `reverse_proxy` to `localhost:{App.Port}`.
- Requires `App.Port` to be non-null. If Port is null, log an error and skip route generation.

**For `serveMode: "fileServer"`:**
- Route matches the domain.
- Handler: `file_server` with root set to `App.InstallDirectory`.
- If `SpaFallback` is true: add `try_files {path} /index.html` rewrite before the file_server handler.
- If `SpaFallback` is false or null: serve files directly, return 404 for missing paths.

**For stopped apps (route disabled via ProxyConfigManager):**
- Route matches the domain.
- Handler: `static_response` with status 503 and body `{"error": "Service is stopped"}`.
- This replaces the normal handler — the route still exists so the domain resolves, but returns 503.
- ProxyConfigManager maintains in-memory state of which routes are active vs disabled. This is NOT persisted on the App entity.

### Route Updates

Routes are regenerated when:
- An app is created (new route added)
- An app is deleted (route removed)
- An app is stopped or started (route swapped between normal and 503)
- An app's routing capability override changes
- An app's port changes (for reverse proxy mode)
- A process starts or crashes (existing event-driven system)

### TLS

All routes use `tls internal` (Caddy's internal CA). This is a platform-wide setting, not per-app.

---

## Capability Catalog — Initial Seed Data

### Behavioral Capabilities

#### `process`

| Field | Purpose |
|-------|---------|
| Slug | `process` |
| DisplayName | Process Management |
| Description | How the app's process is discovered, started, and stopped |
| Category | behavioral |
| Consuming subsystem | ProcessSupervisor |

Configuration type: `ProcessConfiguration`
```json
{
  "discoveryStrategy": "dotnet-runtimeconfig | package-json | manual",
  "gracefulShutdown": true,
  "shutdownTimeoutSeconds": 30,
  "command": null,
  "args": null,
  "workingDirectory": null
}
```

Rules:
- `command` and `args` are ONLY used when `discoveryStrategy` is `"manual"`. They are ignored for other strategies.
- `workingDirectory` defaults to `App.InstallDirectory` when null, regardless of strategy.
- `shutdownTimeoutSeconds` must be > 0. Default is 30.
- `gracefulShutdown` defaults to true. Setting it to false means the process is always hard-killed.

#### `port-injection`

| Field | Purpose |
|-------|---------|
| Slug | `port-injection` |
| DisplayName | Port Injection |
| Description | How the platform communicates the assigned port to the app process |
| Category | behavioral |
| Consuming subsystem | ProcessSupervisor |

Configuration type: `PortInjectionConfiguration`
```json
{
  "environmentVariableName": "ASPNETCORE_URLS",
  "portFormat": "http://localhost:{port}"
}
```

Rules:
- `environmentVariableName` must not be empty.
- `portFormat` must contain `{port}` exactly once.
- This capability triggers port allocation at app creation time.

#### `routing`

| Field | Purpose |
|-------|---------|
| Slug | `routing` |
| DisplayName | Routing |
| Description | How traffic reaches the app through the reverse proxy |
| Category | behavioral |
| Consuming subsystem | ProxyConfigManager (Caddy config generator) |

Configuration type: `RoutingConfiguration`
```json
{
  "domainPattern": "{slug}.collab.internal",
  "serveMode": "reverseProxy",
  "spaFallback": null
}
```

Rules:
- `domainPattern` must contain `{slug}`.
- `serveMode` must be `"reverseProxy"` or `"fileServer"`.
- `spaFallback` is only meaningful for `"fileServer"` mode. Ignored for `"reverseProxy"`.
- When `spaFallback` is null and serveMode is `"fileServer"`, it defaults to false (no fallback).

#### `health-check`

| Field | Purpose |
|-------|---------|
| Slug | `health-check` |
| DisplayName | Health Check |
| Description | HTTP endpoint polled to determine app health |
| Category | behavioral |
| Consuming subsystem | ProcessSupervisor / health monitor |

Configuration type: `HealthCheckConfiguration`
```json
{
  "endpoint": "/health",
  "intervalSeconds": 30,
  "timeoutSeconds": 5,
  "retries": 3
}
```

Rules:
- `endpoint` must start with `/`.
- `intervalSeconds` must be > 0.
- `timeoutSeconds` must be > 0 and <= `intervalSeconds`.
- `retries` defaults to 3 if null. Must be >= 0.
- Health checks only apply to apps with the `process` capability. If an app has `health-check` but not `process`, the health check is inert (nothing to check). This is not an error — it just does nothing.

#### `environment-defaults`

| Field | Purpose |
|-------|---------|
| Slug | `environment-defaults` |
| DisplayName | Environment Variables |
| Description | Environment variables injected when the app process starts |
| Category | behavioral |
| Consuming subsystem | ProcessSupervisor |

Configuration type: `EnvironmentDefaultsConfiguration`
```json
{
  "defaults": {
    "KEY": "value",
    "ANOTHER_KEY": "another_value"
  }
}
```

Rules:
- `defaults` is a flat string→string dictionary. No nested values.
- Empty dictionary is valid (no env vars injected).
- Keys must not be empty strings.
- Values may be empty strings (env var set to empty).
- Dictionary merge rule: operator override keys are merged into type defaults per-key (see "JSON Merge Rules" section).
- This capability only has effect for apps with the `process` capability. If an app has `environment-defaults` but no `process`, the env vars have nowhere to be injected. This is not an error.

#### `restart`

| Field | Purpose |
|-------|---------|
| Slug | `restart` |
| DisplayName | Restart Policy |
| Description | How the platform responds when the app process exits unexpectedly |
| Category | behavioral |
| Consuming subsystem | ProcessSupervisor |

Configuration type: `RestartConfiguration`
```json
{
  "policy": "always"
}
```

Rules:
- `policy` must be one of: `"never"`, `"onCrash"`, `"always"`.
- `"never"`: process is not restarted after exit, regardless of exit code.
- `"onCrash"`: process is restarted only if exit code is non-zero.
- `"always"`: process is restarted after any exit.
- Restart uses exponential backoff (existing ProcessSupervisor behavior). Backoff settings are platform-wide, not per-app.
- Restart only applies when the process was not explicitly stopped by the operator. ProcessSupervisor tracks this in-memory (e.g., a "stopped by operator" state distinct from "crashed").

#### `auto-start`

| Field | Purpose |
|-------|---------|
| Slug | `auto-start` |
| DisplayName | Auto Start |
| Description | Whether the app starts automatically when Collabhost starts |
| Category | behavioral |
| Consuming subsystem | ProcessSupervisor |

Configuration type: `AutoStartConfiguration`
```json
{
  "enabled": true
}
```

Rules:
- Only applies to apps with the `process` capability. Non-process apps (Static Sites) don't need auto-start because they're always "on" (Caddy serves files as long as the route exists and the app is not stopped).
- Auto-start is a configuration concern, not an operational state concern. It determines what happens at Collabhost startup. It is NOT overridden by any persisted "stopped" flag — there is no such flag. See "Interaction with Auto-Start" in the Universal Stop/Start section.

#### `artifact` (future)

The `artifact` capability will hold `InstallDirectory` (moved off the App entity) and related metadata (build command, artifact type, etc.). This capability is defined in the spec but NOT implemented in the initial cards. A separate card will be created when this capability is needed (likely when Card #35 / Collaboard hosting is revisited).

Planned configuration type: `ArtifactConfiguration`
```json
{
  "location": "/path/to/app/artifacts",
  "type": "dotnet-publish | npm-build | binary",
  "buildCommand": "dotnet publish -c Release"
}
```

### Informational Capabilities

Informational capabilities are NOT consumed by any backend subsystem. They are stored, resolved (with overrides), and returned via the API for the frontend to display. The backend does not act on them.

#### `aspnet-runtime`

| Field | Purpose |
|-------|---------|
| Slug | `aspnet-runtime` |
| DisplayName | ASP.NET Runtime |
| Description | .NET runtime and framework version information |
| Category | informational |

Configuration type: `AspNetRuntimeConfiguration`
```json
{
  "targetFramework": "net10.0",
  "runtimeVersion": "10.0.x",
  "selfContained": false
}
```

#### `node-runtime`

| Field | Purpose |
|-------|---------|
| Slug | `node-runtime` |
| DisplayName | Node.js Runtime |
| Description | Node.js version and package manager information |
| Category | informational |

Configuration type: `NodeRuntimeConfiguration`
```json
{
  "nodeVersion": "22.x",
  "packageManager": "npm",
  "buildCommand": "npm run build"
}
```

#### `react-runtime`

| Field | Purpose |
|-------|---------|
| Slug | `react-runtime` |
| DisplayName | React |
| Description | React framework and tooling information |
| Category | informational |

Configuration type: `ReactRuntimeConfiguration`
```json
{
  "version": "18.x",
  "router": "react-router",
  "bundler": "vite"
}
```

---

## Initial App Types — Seed Data

### ASP.NET Core

| Field | Value |
|-------|-------|
| Name | `dotnet-app` |
| DisplayName | ASP.NET Core |
| Description | .NET web application hosted via Kestrel |
| IsBuiltIn | true |

Capabilities and defaults:
```
process:            { "discoveryStrategy": "dotnet-runtimeconfig", "gracefulShutdown": true, "shutdownTimeoutSeconds": 30 }
port-injection:     { "environmentVariableName": "ASPNETCORE_URLS", "portFormat": "http://localhost:{port}" }
routing:            { "domainPattern": "{slug}.collab.internal", "serveMode": "reverseProxy" }
health-check:       { "endpoint": "/health", "intervalSeconds": 30, "timeoutSeconds": 5, "retries": 3 }
environment-defaults: { "defaults": { "ASPNETCORE_ENVIRONMENT": "Production" } }
restart:            { "policy": "always" }
auto-start:         { "enabled": true }
aspnet-runtime:     { "targetFramework": "net10.0", "runtimeVersion": "10.0.x", "selfContained": false }
```

### Node.js App

| Field | Value |
|-------|-------|
| Name | `node-app` |
| DisplayName | Node.js |
| Description | Node.js application |
| IsBuiltIn | true |

Capabilities and defaults:
```
process:            { "discoveryStrategy": "package-json", "gracefulShutdown": true, "shutdownTimeoutSeconds": 30 }
port-injection:     { "environmentVariableName": "PORT", "portFormat": "{port}" }
routing:            { "domainPattern": "{slug}.collab.internal", "serveMode": "reverseProxy" }
health-check:       { "endpoint": "/health", "intervalSeconds": 30, "timeoutSeconds": 5, "retries": 3 }
restart:            { "policy": "always" }
auto-start:         { "enabled": true }
node-runtime:       { "nodeVersion": "22.x", "packageManager": "npm" }
```

### Executable

| Field | Value |
|-------|-------|
| Name | `executable` |
| DisplayName | Executable |
| Description | Generic executable process |
| IsBuiltIn | true |

Capabilities and defaults:
```
process:            { "discoveryStrategy": "manual", "gracefulShutdown": false, "shutdownTimeoutSeconds": 10 }
port-injection:     { "environmentVariableName": "PORT", "portFormat": "{port}" }
routing:            { "domainPattern": "{slug}.collab.internal", "serveMode": "reverseProxy" }
restart:            { "policy": "onCrash" }
auto-start:         { "enabled": false }
```

### React App

| Field | Value |
|-------|-------|
| Name | `react-app` |
| DisplayName | React App |
| Description | React single-page application served as static files |
| IsBuiltIn | true |

Capabilities and defaults:
```
routing:            { "domainPattern": "{slug}.collab.internal", "serveMode": "fileServer", "spaFallback": true }
node-runtime:       { "nodeVersion": "22.x", "packageManager": "npm", "buildCommand": "npm run build" }
react-runtime:      { "version": "18.x", "router": "react-router", "bundler": "vite" }
```

### Static Site

| Field | Value |
|-------|-------|
| Name | `static-site` |
| DisplayName | Static Site |
| Description | Static files served directly by the reverse proxy |
| IsBuiltIn | true |

Capabilities and defaults:
```
routing:            { "domainPattern": "{slug}.collab.internal", "serveMode": "fileServer", "spaFallback": false }
```

---

## API Surface

### App Type Endpoints

```
GET    /api/v1/app-types                — List all app types with their capabilities and default configs
GET    /api/v1/app-types/{externalId}   — Get a single app type with full capability details
POST   /api/v1/app-types                — Create a custom app type (operator-defined)
PUT    /api/v1/app-types/{externalId}   — Update app type (DisplayName, Description, capabilities)
DELETE /api/v1/app-types/{externalId}   — Delete app type (fails 409 if apps exist)
```

### App Endpoints (revised)

```
POST   /api/v1/apps                     — Register a new app
GET    /api/v1/apps                     — List all apps with resolved capabilities
GET    /api/v1/apps/{externalId}        — Get app detail with resolved capabilities
PUT    /api/v1/apps/{externalId}        — Update app (DisplayName, InstallDirectory, capability overrides)
DELETE /api/v1/apps/{externalId}        — Remove app
POST   /api/v1/apps/{externalId}/start  — Start app
POST   /api/v1/apps/{externalId}/stop   — Stop app
POST   /api/v1/apps/{externalId}/kill   — Force kill app process
GET    /api/v1/apps/{externalId}/logs   — Get app logs (ring buffer)
GET    /api/v1/apps/{externalId}/status — Get app runtime status (PID, uptime, health)
```

### Capability Endpoints

```
GET    /api/v1/capabilities             — List all capabilities (catalog)
```

No CRUD for capabilities — they are system-defined seed data.

### Lookup Endpoints (revised)

```
GET    /api/v1/lookups/app-types        — Lightweight list (id, name, displayName) for dropdowns
```

The existing `/api/v1/lookups/restart-policies` endpoint is removed.

### Response Shape — App Detail

When the frontend requests an app, the response includes resolved capabilities and derived runtime state. This is the **bridge** — the API handler aggregates data from the database (capabilities), ProcessSupervisor (process state), and ProxyConfigManager (route state), and produces a unified response.

```json
{
  "id": "01ABC...",
  "name": "collaboard-api",
  "displayName": "Collaboard API",
  "appType": {
    "id": "01DEF...",
    "name": "dotnet-app",
    "displayName": "ASP.NET Core"
  },
  "installDirectory": "C:/Projects/collab/collabhost/apps/collaboard-api",
  "port": 52341,
  "registeredAt": "2026-03-30T15:00:00Z",
  "runtime": {
    "process": {
      "state": "running",
      "pid": 12345,
      "uptimeSeconds": 3600,
      "restartCount": 0
    },
    "route": {
      "state": "active",
      "domain": "collaboard-api.collab.internal"
    }
  },
  "capabilities": {
    "process": {
      "category": "behavioral",
      "displayName": "Process Management",
      "resolved": {
        "discoveryStrategy": "dotnet-runtimeconfig",
        "gracefulShutdown": true,
        "shutdownTimeoutSeconds": 30
      },
      "hasOverrides": false
    },
    "environment-defaults": {
      "category": "behavioral",
      "displayName": "Environment Variables",
      "resolved": {
        "defaults": {
          "ASPNETCORE_ENVIRONMENT": "Production",
          "ConnectionStrings__Board": "Data Source=..."
        }
      },
      "hasOverrides": true
    },
    "aspnet-runtime": {
      "category": "informational",
      "displayName": "ASP.NET Runtime",
      "resolved": {
        "targetFramework": "net10.0",
        "runtimeVersion": "10.0.x",
        "selfContained": false
      },
      "hasOverrides": false
    }
  }
}
```

**Rules for the response:**
- `capabilities` is a dictionary keyed by capability slug.
- Each capability entry includes `category`, `displayName`, `resolved` (the merged configuration), and `hasOverrides` (bool indicating whether the operator has customized this capability).
- The `resolved` object is the fully merged configuration. The frontend does not need to perform any merging.
- All capabilities for the app's type are included, even if the operator has not overridden them.
- `runtime` is the bridge's derived state section. It is assembled by the API handler from in-memory core system state:
  - `runtime.process` is present only for apps with the `process` capability. Sourced from ProcessSupervisor's `GetProcess()`. For non-process apps, `runtime.process` is null/absent.
  - `runtime.route` is present only for apps with the `routing` capability. Sourced from ProxyConfigManager. Shows whether the route is active or disabled (503).
  - The exact shape of `runtime` may evolve — the bridge is free to derive additional fields as needed. The key rule is: **runtime state is always derived, never persisted on the App entity.**

### Response Shape — App Type Detail

```json
{
  "id": "01DEF...",
  "name": "dotnet-app",
  "displayName": "ASP.NET Core",
  "description": ".NET web application hosted via Kestrel",
  "isBuiltIn": true,
  "capabilities": {
    "process": {
      "category": "behavioral",
      "displayName": "Process Management",
      "defaults": {
        "discoveryStrategy": "dotnet-runtimeconfig",
        "gracefulShutdown": true,
        "shutdownTimeoutSeconds": 30
      }
    },
    "port-injection": {
      "category": "behavioral",
      "displayName": "Port Injection",
      "defaults": {
        "environmentVariableName": "ASPNETCORE_URLS",
        "portFormat": "http://localhost:{port}"
      }
    }
  }
}
```

### Request Shape — Create App

```json
{
  "name": "collaboard-api",
  "displayName": "Collaboard API",
  "appTypeId": "01DEF...",
  "installDirectory": "C:/Projects/collab/collabhost/apps/collaboard-api",
  "capabilityOverrides": {
    "environment-defaults": {
      "defaults": {
        "ConnectionStrings__Board": "Data Source=...",
        "Admin__AuthKey": "01KMG..."
      }
    },
    "health-check": {
      "endpoint": "/api/health"
    }
  }
}
```

**Rules:**
- `capabilityOverrides` is optional. If omitted, the app uses all type defaults.
- Each key in `capabilityOverrides` is a capability slug. The value is the override JSON.
- The API validates that each capability slug in the overrides actually belongs to the app's type. Unknown slugs return 400.
- The API validates the override JSON against the capability's C# config type. Invalid JSON returns 400.
- Overrides are partial — only include fields being changed. The API does NOT expect a full copy.
- CapabilityConfiguration rows are created only for capabilities that have overrides.

### Request Shape — Update App

```json
{
  "displayName": "Collaboard API (Production)",
  "installDirectory": "C:/Projects/collab/collabhost/apps/collaboard-api",
  "capabilityOverrides": {
    "health-check": {
      "endpoint": "/healthz"
    },
    "environment-defaults": null
  }
}
```

**Rules:**
- All fields are optional. Only provided fields are changed.
- Setting a capability override to `null` explicitly removes the override (deletes the CapabilityConfiguration row), resetting to type defaults.
- Setting a capability override to `{}` (empty object) is different from `null` — it creates/updates a CapabilityConfiguration row with an empty override, which means "I explicitly set this but changed nothing." This should be treated the same as `null` (delete the row). The API normalizes empty overrides to deletion.
- `installDirectory` can only be changed when the app is stopped. If the app is running, return 409.
- `name` is NOT included — it is immutable.

---

## Frontend Contract

### Capability → Widget Mapping

Each capability slug maps to exactly one frontend widget component. The frontend maintains a registry of widgets keyed by slug.

```
"process"              → ProcessWidget
"port-injection"       → PortInjectionWidget
"routing"              → RoutingWidget
"health-check"         → HealthCheckWidget
"environment-defaults" → EnvironmentDefaultsWidget
"restart"              → RestartWidget
"auto-start"           → AutoStartWidget
"aspnet-runtime"       → AspNetRuntimeWidget
"node-runtime"         → NodeRuntimeWidget
"react-runtime"        → ReactRuntimeWidget
```

**Rules:**
- If the frontend encounters a capability slug it does not have a widget for, it renders a fallback: a read-only JSON viewer showing the raw resolved configuration. This ensures new backend capabilities don't break the frontend — they just display as raw data until a widget is built.
- Behavioral capabilities are rendered in a "Configuration" section of the app detail view.
- Informational capabilities are rendered in a "Runtime Info" section.
- On the create form: when the operator selects an app type, the form queries the app type's capabilities and renders the appropriate widgets with type defaults pre-filled. The operator can modify values (which become overrides) or leave them as-is (no override created).
- On the app card (list view): the card shows a summary. Which capabilities appear on the card is a frontend design decision — but at minimum, the app type display name, status (running/stopped), and domain should be visible.

### Dashboard Controls

The following controls are rendered based on capability composition and derived runtime state:

| Control | Shown when |
|---------|-----------|
| Start button | App has actionable capabilities that are not active (process stopped, route disabled) |
| Stop button | App has actionable capabilities that are active (process running, route active) |
| Restart button | App has `process` capability AND process is running |
| Force Kill button | App has `process` capability AND process is running |
| Domain link | App has `routing` capability AND route is active |

**Rules:**
- Start/Stop is shown for ALL apps. This is universal. The UI reads the `runtime` state from the bridge response to determine which button to show.
- Restart and Force Kill are only for process-based apps.
- The domain link is constructed from the routing capability's resolved `domainPattern` with `{slug}` replaced by the app's name. Protocol is always `https://`.
- When the route is disabled (503), the domain link may still be shown but visually marked as inactive (grayed out, strikethrough, etc. — frontend design decision).
- The UI does NOT read a persisted `isStopped` flag. All state comes from the `runtime` section of the bridge response, which is derived from in-memory core system state.

---

## Deploy Directory Convention

Managed app artifacts live under `apps/` at the Collabhost repo root.

```
collabhost/
  apps/                          # gitignored
    collaboard-api/              # dotnet publish output
    collaboard/                  # frontend dist output
    minio/                       # off-the-shelf binary
```

**Rules:**
- `apps/` must be added to `.gitignore`.
- Each app's directory is named by its slug (`App.Name`).
- The `App.InstallDirectory` field stores the absolute path. The `apps/` convention is a recommendation, not enforced by the platform. Operators can point InstallDirectory anywhere.
- Collabhost does NOT create the app directory. The operator (or a build process) is responsible for placing artifacts in the directory before starting the app.
- If the InstallDirectory does not exist at process start time, the discovery strategy fails with a clear error.

---

## IdentifierCatalog Updates

All built-in entities use hardcoded Guids in the IdentifierCatalog. This prevents magic string lookups in C# code.

```csharp
public static class IdentifierCatalog
{
    public static class AppTypes
    {
        public static readonly Guid DotNetApp = Guid("...");
        public static readonly Guid NodeApp = Guid("...");
        public static readonly Guid Executable = Guid("...");
        public static readonly Guid ReactApp = Guid("...");
        public static readonly Guid StaticSite = Guid("...");
    }

    public static class Capabilities
    {
        public static readonly Guid Process = Guid("...");
        public static readonly Guid PortInjection = Guid("...");
        public static readonly Guid Routing = Guid("...");
        public static readonly Guid HealthCheck = Guid("...");
        public static readonly Guid EnvironmentDefaults = Guid("...");
        public static readonly Guid Restart = Guid("...");
        public static readonly Guid AutoStart = Guid("...");
        public static readonly Guid AspNetRuntime = Guid("...");
        public static readonly Guid NodeRuntime = Guid("...");
        public static readonly Guid ReactRuntime = Guid("...");
    }
}
```

The existing `IdentifierCatalog.AppTypes` entries for Executable, NpmPackage, StaticSite are replaced. NpmPackage is renamed to NodeApp. The Guids change (no backwards compat).

The existing `IdentifierCatalog.RestartPolicies` section is removed entirely.

---

## What Gets Removed

| Item | Reason |
|------|--------|
| `AppTypeBehavior` class | Replaced by capability queries |
| `RestartPolicy` entity + lookup table | Absorbed into `restart` capability |
| `EnvironmentVariable` entity + table | Absorbed into `environment-defaults` capability |
| `IdentifierCatalog.RestartPolicies` | No longer exists |
| `StringCatalog` entries for RestartPolicy | No longer exists |
| App entity fields: Command, Arguments, WorkingDirectory, HealthEndpoint, UpdateCommand, AutoStart, RestartPolicyId, IsStopped | Moved to capabilities, derived from in-memory state, or removed |
| `/api/v1/lookups/restart-policies` endpoint | No longer exists |
| Frontend per-type visibility logic (isProtected, isRoutable, etc.) | Replaced by capability-driven rendering |
| Frontend hardcoded type checks | Replaced by widget registry |

### Renames

| Old | New | Reason |
|-----|-----|--------|
| `ProcessSupervisor.GetStatus()` | `ProcessSupervisor.GetProcess()` | Returns `ManagedProcess`, not a status value. Name should reflect what it returns. |

---

## 12-Factor Alignment

| Factor | Status | How this spec addresses it |
|--------|--------|---------------------------|
| III Config | **Aligned** | All config via env vars through `environment-defaults` and `port-injection` capabilities |
| IV Backing services | **Aligned** | Connection strings injected via `environment-defaults` |
| V Build/release/run | **Gap** | Out of scope. Future work — possibly GitHub Actions integration. UpdateCommand removed. |
| VI Processes | **Aligned** | Stateless process model via `process` capability |
| VII Port binding | **Aligned** | Platform-coordinated port binding via `port-injection` capability |
| VIII Concurrency | **Future** | Single process per app. Scaling capability deferred. |
| IX Disposability | **Addressed** | Graceful shutdown via `process` capability config. Card #37 for implementation. |
| XI Logs | **Aligned** | stdout/stderr capture in ring buffer. Long-term storage deferred. |
| XII Admin processes | **Future** | One-off / scheduled task capability deferred. |

---

## Alternatives Considered

### Keep AppTypeBehavior, add .NET-specific branches
Rejected. Hardcoded predicates don't scale. Every new type requires C# code changes across multiple files. Violates the data-driven principle.

### Behavior-based types (Process vs Static vs Worker) with runtime config
Considered. Too generic — pushes too much configuration onto the operator. Runtime-based types with capability composition gives both convenience (sensible defaults) and flexibility (override anything).

### Copy-on-create configuration (snapshot defaults at app creation)
Considered. Simpler implementation, but type default updates don't reach existing apps. Live inheritance with overrides chosen instead — the type definition stays the single source of truth, operator overrides are preserved. Updating a type default in one place updates all apps that haven't explicitly overridden that value.

### Schema-driven UI (form builder from capability metadata)
Considered. Would allow fully auto-generated forms from JSON schema metadata on each capability. Rejected — too complex, produces generic/ugly forms, and is a form builder rabbit hole. The capability slug → frontend widget mapping is simpler and produces purpose-built UI for each capability.

### Separate EnvironmentVariable table alongside capabilities
Considered. Would keep the existing per-app env var table for operator-specified vars, with capabilities providing defaults. Rejected — two sources of env vars creates precedence confusion and data model complexity. All env vars live in the `environment-defaults` capability, with operator overrides via the standard override mechanism.

---

## Sign-off

- [x] Approved — 2026-03-30 (full spec discussion completed)
