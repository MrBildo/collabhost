# Collabhost Knowledge Base

Consolidated coding conventions, standards, and patterns for the Collabhost project. This is the single source of truth — all agents and sub-agents MUST follow these rules when writing code.

---

## 1. .NET / C# Conventions

### Naming

- **File-scoped namespaces** — always, no block-scoped namespaces
- **PascalCase** for classes, structs, enums, properties, methods, constants
- **`IPascalCase`** for interfaces (prefix `I`)
- **`_camelCase`** for private fields
- **`var`** everywhere — never explicit types unless necessary for disambiguation
- **Verbose naming** — full words, no abbreviations:
  - `Authorization` not `Auth`
  - `EnvironmentVariables` not `EnvVars`
  - `Configuration` not `Config`
  - `Application` not `App` (except the domain entity `App` which is the product name)
- **Settings suffix** for configuration classes (not Options): `ProxySettings`, `SchedulerSettings`

### Code Style

- **Primary constructors** for DI injection
- **Pattern matching** — `is null`, `is not null` (never `== null`)
- **Collection expressions** — `[]` for empty, `[.. source]` for spread
- **Expression-bodied members** for single-expression methods/properties (IDE0022 promoted to error)
- **No XML doc comments** unless explicitly requested
- **Guard clauses** at method entry:
  - `ArgumentNullException.ThrowIfNull(param)`
  - `ArgumentException.ThrowIfNullOrWhiteSpace(param)`
  - `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(param)`
- **Blank line between all members** — methods, properties, fields, constructors in classes/structs/interfaces
- **C# 14 extension blocks** — use the new syntax, not traditional `static class` + `this` parameter:

```csharp
// Correct
extension(IServiceCollection services)
{
    public IServiceCollection AddCommandDispatcher()
    {
        services.AddScoped<CommandDispatcher>();
        return services;
    }
}

// Wrong — do not use
public static class ServiceExtensions
{
    public static IServiceCollection AddCommandDispatcher(this IServiceCollection services) { ... }
}
```

- **Multi-line parenthesis formatting** — when a parameter list wraps, `(` goes on its own line (indented), each parameter on its own line, `)` on its own line. Applies to records, method declarations, method calls, and constructor base calls. `dotnet format` does NOT enforce this — agents must follow manually:

```csharp
// Correct — wrapping params
public static App Register
(
    AppSlugValue name,
    string displayName,
    Guid appTypeId,
    string installDirectory,
    bool autoStart
)
{
    // ...
}

// Also correct — fits on one line, no wrapping needed
public record Response(string ExternalId);
```

### File Organization

- **Consolidated `_` prefix files** — group related types into single `_`-prefixed files. The `_` signals "module of related types, not a single type":
  - `_Commands.cs` — dispatcher, result types, registration
  - `_Authorization.cs` — auth helpers, role types
  - `_BaseEntities.cs` — `Entity`, `AggregateRoot`, `LookupEntity`
  - `_Catalogs.cs` — `IdentifierCatalog`, `StringCatalog`
  - `_Lookups.cs` — all lookup entity types
  - `_Module.cs` — `IFeatureModule` implementation per domain
  - `_ServiceRegistration.cs` — DI registration for a service group

- **Vertical slice feature files** — one file per operation (e.g., `Create.cs`, `GetAll.cs`, `Delete.cs`). Each file contains three top-level types:
  1. **Static endpoint class** — `Request`/`Response` records + `HandleAsync` (HTTP layer only, dispatches to command)
  2. **Command record** — implements `ICommand<TResult>`
  3. **Handler class** — implements `ICommandHandler<TCommand, TResult>`, contains all business logic

```csharp
// Features/Apps/Create.cs — complete vertical slice structure
namespace Collabhost.Api.Features.Apps;

public static class Create
{
    public record Request
    (
        string Name,
        string DisplayName
    );

    public record Response(string ExternalId);

    public static async Task<Results<Created<Response>, ProblemHttpResult>> HandleAsync
    (
        Request request,
        CommandDispatcher dispatcher,
        CancellationToken ct
    )
    {
        var command = new CreateCommand(request.Name, request.DisplayName);
        var result = await dispatcher.DispatchAsync(command, ct);

        return result.IsSuccess
            ? TypedResults.Created($"/api/v1/apps/{result.Value}", new Response(result.Value!))
            : TypedResults.Problem(result.ErrorMessage, statusCode: 400);
    }
}

public record CreateCommand(string Name, string DisplayName) : ICommand<string>;

public class CreateCommandHandler(CollabhostDbContext db) : ICommandHandler<CreateCommand, string>
{
    private readonly CollabhostDbContext _db = db ?? throw new ArgumentNullException(nameof(db));

    public async Task<CommandResult<string>> HandleAsync(CreateCommand command, CancellationToken ct = default)
    {
        // Business logic here
        return CommandResult<string>.Success(externalId);
    }
}
```

- **Feature modules** under `Features/` grouped by domain (`Apps/`, `Proxy/`, `System/`, `Lookups/`)
- **`Program.cs`** is a thin composition root — service wiring and feature module auto-discovery only

### Domain Patterns

**Entity base classes:**

| Base Class | Key | Purpose |
|---|---|---|
| `Entity` | `Guid Id` (auto-generated) | Base for all entities |
| `AggregateRoot` | `Entity` + `string ExternalId` (ULID) | Aggregate roots exposed via API |
| `LookupEntity` | `Guid Id` (seeded) + `Name` + `DisplayName` + `Ordinal` | Reference data |

**Factory methods for entity creation** — never `new Entity(...)`:

```csharp
// Correct
var app = App.Register(name, displayName, appTypeId, ...);

// Wrong
var app = new App { Name = name, DisplayName = displayName };
```

- **Private setters** — mutation via methods only (`app.AssignPort(port)`, `app.UpdateConfiguration(...)`)
- **Protected parameterless constructor** for EF Core: `protected App() { }`

**Never fabricate GUIDs or ULIDs.** Use `dotnet run --file tools/generate-ids.cs` to generate real identifiers for seed data, catalog constants, and migrations. Placeholder-style IDs (`a1234567-...`, sequential patterns) are never acceptable.

**Lookup tables, not enums** for persisted/displayed values. Lookup entities are seeded with fixed Guid IDs and have `Name`, `DisplayName`, `Description`, `Ordinal`, `IsActive`.

**Backend is the source of truth for all option/enum-like values.** Any field with a fixed set of valid values (dropdowns, selects, mode switches) — e.g., restart policies, serve modes, discovery strategies — must follow this chain:

1. **Define** as a backend lookup (lookup table or catalog constant)
2. **Reference** from backend logic (never raw string literals — use the lookup/catalog)
3. **Expose** valid values + display labels via API
4. **Consume** from the API on the frontend — never hardcode option lists in widget components

**Catalog pattern** — two companion static classes for lookup constants:

```csharp
// IdentifierCatalog — fixed Guid IDs for lookup references
public static class IdentifierCatalog
{
    public static class AppTypes
    {
        public static readonly Guid Executable = new("acdb6994-2c22-42f5-bf89-68c42c9f980c");
        public static readonly Guid StaticSite = new("7dc8cc9f-1600-447a-85f4-cbc0fc44e6fc");
    }
}

// StringCatalog — machine-readable Name constants (match DB Name column)
public static class StringCatalog
{
    public static class AppTypes
    {
        public const string Executable = "Executable";
        public const string StaticSite = "StaticSite";
    }
}
```

**Value objects** with `CanCreate()` / `Create()` pattern:

```csharp
public sealed partial class AppSlugValue
{
    public string Value { get; }

    // Validation without exceptions — returns tuple
    public static (bool IsValid, string? Error) CanCreate(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return (false, "App name is required.");
        }
        // ... more validation
        return (true, null);
    }

    // Creation that throws on invalid input
    public static AppSlugValue Create(string slug)
    {
        var (isValid, error) = CanCreate(slug);
        return !isValid ? throw new ArgumentException(error, nameof(slug)) : new AppSlugValue(slug);
    }

    // Private constructor — only Create() can instantiate
    private AppSlugValue(string slug)
    {
        Value = slug.Trim().ToLowerInvariant();
    }

    // Implicit string conversion
    public static implicit operator string(AppSlugValue slug) => slug.Value;

    public override string ToString() => Value;
}
```

### Data Access

- **Guid** internal IDs, **ULID** external IDs (string representation)
- **EF Core shadow properties** for audit (`CreatedAt`/`UpdatedAt`) via `AuditInterceptor`
- **`SingleAsync`** (not `FirstAsync`) for single-result entity lookups by ID
- **`SqlQuery<T>()`** with SQL JOINs for read queries — never return entities from query handlers
- **DbSets for aggregate roots only** — child entities via `_db.Set<T>()`
- **MaxLength** always has inline comment explaining rationale: `// ULID string representation`

### Command Pattern

- **Unified `ICommand<TResult>` / `ICommandHandler<TCommand, TResult>`** for ALL operations (reads AND writes)
- **`CommandDispatcher`** with type-inferring `DispatchAsync` — resolves handlers via DI
- **`Empty`** struct for void-result commands (`ICommand<Empty>`)
- **`CommandResult<T>`** with `Success(value)` / `Fail(errorCode, errorMessage)` factory methods
- **`CommandResult`** (non-generic) with `Success()` / `Fail(errorCode, errorMessage)` for void results
- **`AddCommandDispatcher()`** — auto-scans assembly for handler registrations

### API Endpoints

- **TypedResults always** (not `Results`) for compile-time safety
- **`Results<T1, T2, ...>`** union return types on handler signatures
- **`Results.StatusCode(403)`** not `Results.Forbid()` (no ASP.NET auth middleware)
- **`IFeatureModule`** for route group mapping, auto-discovered by reflection
- Thin **`Program.cs`** — composition root only, no business logic

### SSE Pattern

For long-running operations (app updates, future log streaming):

1. Validate and return normal HTTP errors (404/400/409) **before** committing to SSE
2. Set `Content-Type: text/event-stream`, `Cache-Control: no-cache`, then `StartAsync()`
3. Use `Channel<T>` for thread-safe ordered event delivery from callbacks — never fire-and-forget writes to `HttpContext.Response`
4. Complete the channel and await the consumer task before sending the final result event
5. Shell-wrap user-provided commands: `cmd.exe /c` (Windows), `/bin/sh -c` (Linux)

### Auth Model

- Header-based: **`X-User-Key`** (ULID)
- Roles: `Administrator`, `HumanUser`, `AgentUser`
- Custom logic — no ASP.NET auth middleware
- Auto-generate and log admin key on first run
- Admin seed from config or generated key

### Analyzers

Four Roslyn analyzers enforced across all projects:

| Analyzer | Package | Scope |
|---|---|---|
| .NET Analyzers | Built-in | `AnalysisLevel: latest-Recommended` |
| Meziantou | `Meziantou.Analyzer` | Broad C# quality |
| VS.Threading | `Microsoft.VisualStudio.Threading.Analyzers` | Async patterns |
| SonarAnalyzer | `SonarAnalyzer.CSharp` | Code quality |

**Configuration:**
- `.editorconfig` — rule severities
- `Directory.Build.props` — shared properties, analyzer packages, global suppressions (`CA1873`, `S3881`, `S6966`)
- `Directory.Build.targets` — test-specific suppressions (`CA1707` for test method naming)

**Philosophy:** If worth enforcing, make it `error`. Warnings only for gentle nudges. Suggestions consumed via IDE only.

**Promoted to error (curated individually):**

| Rule | Purpose |
|---|---|
| MA0001 | StringComparison required |
| MA0029 | Combine LINQ methods |
| MA0036 | Make class static |
| MA0040 | Pass CancellationToken |
| MA0042 | Async disposal |
| MA0053 | Seal classes |
| MA0076 | Explicit culture for ToString |
| MA0190 | Frozen collections / partial properties |
| ASP0027 | Route parameter mismatch |
| IDE0022 | Expression-bodied members |
| IDE0270 | Simplify null check |
| VSTHRD200 | Async method naming suffix |
| CS8600-CS8767 | All nullability diagnostics |

**Suppressed (with rationale):**

| Rule | Rationale |
|---|---|
| CA1708, MA0038, MA0041 | False positives on C# 14 extension blocks |
| CA1822 | Extension block conflicts (MA0038 Meziantou equivalent also suppressed) |
| MA0003 | Enum descriptions (only one internal enum, not user-facing) |
| MA0007 | Trailing commas (style preference: not enforced) |
| MA0018 | Static on generic types (false positive on `CommandResult<T>.Success()`) |
| MA0176 | Guid creation optimization (false positive on catalog constants and EF migrations) |

**Gentle-nudge warnings (not errors):** MA0026 (TODO comments), MA0051 (method length), IDE0161

### Testing

- **xUnit + Shouldly** — Arrange-Act-Assert pattern
- **`WebApplicationFactory`** with in-memory SQLite for integration tests (`Collabhost.Api.Tests`)
- **Aspire smoke tests** in `Collabhost.AppHost.Tests` — real Kestrel, real SQLite, real process runner
- Test file naming: `*.Tests.cs`, test method naming allows underscores (`CA1707` suppressed in test projects)
- **NEVER auto-fix tests during analyzer changes** — investigate failures as behavioral signals

### Build and Verification

All must pass before reporting done:

```powershell
dotnet build                                      # 0 errors, 0 warnings
dotnet format --verify-no-changes                 # formatting clean
dotnet format Collabhost.slnx --verify-no-changes --severity info  # suggestion check (report NEW suggestions to user)
dotnet test                                       # all pass (includes Aspire smoke tests)
```

- Never modify `.editorconfig` to work around conflicts — restructure code or use `#pragma` instead
- Never use `dotnet-script` — use `dotnet run --file <file>.cs` for single-file execution

---

## 2. TypeScript / React Conventions

### Code Style

- **2-space indentation** everywhere
- **Functional components only** — no class components
- **Hooks** for state (`useState`, `useReducer`) and side effects (`useEffect`)
- **Named exports** for types and utilities, **default export** for page/route components (conventional)
- **PascalCase** file names for components (`AppCard.tsx`, `StatusBadge.tsx`)
- **camelCase** file names for hooks (`useApps.ts`, `useSystem.ts`)
- **Never `any`** — always type everything explicitly
- **No `I` prefix** on interfaces (idiomatic TypeScript — `AppConfig` not `IAppConfig`)

### Styling

- **Tailwind CSS v4** + `@tailwindcss/vite` plugin (NOT PostCSS)
- **shadcn/ui** components from `@/components/ui/` (base-nova style, Radix primitives)
- **`cn()`** utility for Tailwind class merging (`clsx` + `tailwind-merge`)
- **Design tokens** from Collaboard theme: cyan primary, orange accent
- **Typography:** Geist (body and headings via `@fontsource-variable/geist`)
- **Dark/light mode** via `next-themes`, persisted automatically

### Data and State

- **Axios instance** (`api`) with `X-User-Key` header injected from `localStorage`
- **TanStack Query** for all API calls — queries and mutations
- **5s polling** for status queries (`refetchInterval: 5000`)
- **Custom hooks** extracted per feature:
  - `useApps()`, `useAppStatus(id)`, `useStartApp()`, `useStopApp()`, `useRestartApp()`, `useCreateApp()`
  - `useRoutes()`, `useSystem()`, `useLookups()`
- **Invalidate queries on mutation success** via `queryClient.invalidateQueries()`
- **Zod** for schema validation where needed

### Project Structure

```
frontend/src/
  components/     # Reusable UI components
  components/ui/  # shadcn/ui base components
  hooks/          # Custom hooks (one per feature area)
  routes/         # Page components (route-level)
  types/          # TypeScript type definitions
  lib/            # Utilities (api.ts, utils.ts, format.ts)
```

### Build and Verification

All must pass before reporting done:

```powershell
npm run build          # tsc typecheck + Vite bundle
npm run lint           # ESLint — 0 errors
npm run format:check   # Prettier — clean
npm run test           # Vitest — all pass
```

### Vendor Abstraction

- Use generic terms in UI (e.g., "Proxy" not "Caddy") unless displaying a specific product name is necessary for the operator

---

## 3. General Conventions

### Git

- **Conventional commits:** `feat:`, `fix:`, `chore:`, `docs:`, `refactor:`
- **Branch naming:** `feature/`, `bugfix/`, `hotfix/`
- **Squash merge** to `main` via PR
- **Commit everything** — `git status` must be clean when done. Docs, CLAUDE.md, .gitignore, config — all committed with current work
- **Never commit directly to main** — always feature branch + PR
- Never commit secrets (`.env`, `.agents.env`, credentials)

### Definition of Done

A feature is done when ALL of the following pass:

**Backend:**
- `dotnet build Collabhost.slnx --no-incremental` — 0 errors, 0 warnings. **MUST use `--no-incremental`** to match Visual Studio behavior (incremental builds skip compilation and hide warnings). **MUST build the solution**, not individual projects. **Read the FULL build output** — this includes CSC-level warnings, MSBuild warnings, and analyzer warnings, not just the summary line. ANY warning from ANY source must be surfaced and addressed.
- `dotnet format --verify-no-changes` — clean
- `dotnet format Collabhost.slnx --verify-no-changes --severity info` — **suggestion check.** Surfaces Info/suggestion-level diagnostics (the green squigglies from Visual Studio). If NEW suggestions appear that were not present before your changes, **stop and report them to the user.** Do not auto-fix, do not ignore. Each suggestion requires a resolution decision from Bill (promote to error, demote to none, or defer). Agents may address suggestions that are already resolved to error-level in `.editorconfig` the same way they handle any other build error.
- `dotnet test` — all pass. Same rule: read the full output, surface ANY warnings from the test run (build warnings appear here too).

**Frontend:**
- `npm run build` — typecheck + bundle clean
- `npm run lint` — 0 errors
- `npm run format:check` — clean
- `npm run test` — all pass

**Observable:** Feature must be observable in the running application, not just tests green.

### Sub-Agent Rules

- **Model:** Always use `model: "opus"` (Opus High)
- **Skills:** MUST invoke `dotnet-dev` for C# tasks, `typescript-dev` for TypeScript tasks
- **MUST read and follow this document** (`COLLABHOST_KB.md`) — all conventions apply
- **Backend agents MUST run full verification:** `dotnet build`, `dotnet format --verify-no-changes`, `dotnet format --verify-no-changes --severity info` (suggestion check), `dotnet test`
- **Frontend agents MUST run:** `npm run build`, `npm run lint`, `npm run format:check`, `npm run test`
- **Never auto-fix lint errors** — report to user
- **Never auto-fix test failures during analyzer work** — investigate failures as behavioral signals
- **No frontend hacks for backend concerns** — if a frontend agent encounters a problem that should be fixed on the backend (wrong datetime format, missing endpoint, hardcoded lookup data), report the gap. No translation layers, hardcoded constants, or data fixups that belong server-side
- **Max 3 follow-up rounds** before escalating to user
- **Standardized report format** — every sub-agent must return:

```
## Report: <card or task title>

### Summary
<1-2 sentence verdict>

### Deliverable Status
| Deliverable | Status | Notes |
|---|---|---|
| <item> | Done / Partial / Missing | <detail> |

### Verification
- Build: <pass/fail/not run — which sub-project(s)>
- Smoke test: <pass/fail/not run>

### Files Touched
- <path> — <created/modified/read> — <what changed>

### Gaps & Issues
1. <issue description>

### Convention Violations
<list or "None">

### Recommendation
<next steps, move to Review, stays in Ready, etc.>
```

### Dispatch Rules

- **Spec first** — write specs to `.agents/specs/` before dispatching. No dispatch without a spec
- **Include ALL context** the child needs — it has no memory of this session
- **Partition parallel work by resource** (files touched), not by task. Two cards that edit the same files must go to the same agent
- **Use git worktrees** for parallel agents on same repo:
  ```powershell
  git worktree add ../<repo>-wt-<short-name> -b feature/<branch-name> <start-point>
  ```
  Each worktree needs its own dependency install. The `.git` store is shared.

### Context Window Management

- **Stay scoped** to `backend/` OR `frontend/` — don't read source from the other subsystem
- Cross-subsystem features get separate tasks
- Reading both wastes context and risks confusion

### Path Conventions

- **Relative paths** in committed files — never hardcode absolute paths
- **Cross-project references:** `../` relative paths (`../collaboard`, `../ecosystem`)
- **Absolute paths only** in gitignored local config (`.agents.env`, local settings)

### Safety

1. **Safety over speed** — never auto-fix lint errors, report to user
2. **No destructive actions** without explicit permission
3. **Ask, don't guess** — if uncertain about scope, intent, or approach, stop and ask
4. **On any issues, errors, or unexpected behavior** — stop and ask
5. **Max 3 follow-ups before escalation** to user
