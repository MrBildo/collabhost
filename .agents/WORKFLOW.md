# Collabhost — Agent Workflow

## Current State (updated 2026-03-31)

- **Phase:** Capability Architecture — COMPLETE. Post-MVP production design.
- **Research:** Complete. 8 projects investigated, synthesis produced.
- **Capability Architecture (Card #36):** Full implementation across 8 PRs (#12–#18). Replaced hardcoded AppTypeBehavior with data-driven capability composition model. See `.agents/specs/capability-architecture.md` for full spec.
- **Key patterns:** Capability composition (AppType → AppTypeCapability → Capability), live inheritance with overrides (CapabilityConfiguration), configuration merge service (ICapabilityResolver), Core vs Bridge vs UI architecture (ProcessSupervisor/ProxyConfigManager are core systems, API endpoints are the bridge, frontend consumes bridge output), discovery strategies (dotnet-runtimeconfig, package-json, manual), universal stop/start with derived state (no persisted flags), capability widget registry (slug → component family).
- **Build:** 95 backend tests (87 integration + 8 Aspire smoke), 38 frontend tests, all passing.
- **Follow-up cards:** #42 (typed JSON wrapper), #47 (API-driven label maps), #48 (8 UI feedback items from e2e testing — includes artifact capability blocker).

### ARCHITECTURE.md staleness notes
The following sections in `.agents/temp/architecture-mockup/ARCHITECTURE.md` are **stale** after card #20 and #21:
- **Project structure tree** — Auth/ folder removed, files consolidated with `_` prefix, DbContext extensions merged, FeatureModuleExtensions renamed
- **Feature file anatomy (query example)** — queries now use unified `ICommand<TResult>` / `ICommandHandler` / `CommandDispatcher` pattern, not separate `QueryResult<T>` / `Handler`
- **Handler auto-registration** — no more separate query handler registration; `AddCommandDispatcher()` handles all handlers
- **Shared queries via DbContext extensions** — code example shows old `public static class` syntax; now uses C# 14 extension blocks directly in `CollabhostDbContext.cs`
- **Infrastructure registration** — no longer in `Program.cs`; extracted to `Services/_ServiceRegistration.cs`

CLAUDE.md is the up-to-date source for conventions and patterns.

## Key Context for New Agents

- **Collaboard board:** slug `collabhost`, auth key in `.agents.env`
- **Collaboard API:** `http://localhost:8080/api/v1` (useful for attachment uploads via curl when base64 is too large for MCP tool parameters)
- **First managed app:** Collaboard, installed at `C:\Users\mrbil\AppData\Local\Collaboard`. A .NET backend + React SPA served as a single exe.
- **Research repos:** Shallow clones at `.agents/research/{coolify,dokploy,caprover,dokku,swiftwave,kubero,portainer,caddy}/`. Each has a `FINDINGS.md`.
- **Aspire:** AppHost at `backend/Collabhost.AppHost/`. Dashboard at `https://localhost:17889`. API at `http://localhost:58400`. Use `aspire start` to launch, `aspire describe` to check status.
- **Key documents:**
  - `roadmap/MVP.md` — full plan with all decisions resolved
  - `research/SYNTHESIS.md` — cross-cutting research synthesis with patterns to adopt/avoid
  - `research/*/FINDINGS.md` — individual project deep-dives
  - `specs/TEMPLATE.md` — use for new specs

## Planning

1. Check the Collaboard board (`collabhost` slug) for current state
2. Pick up cards from **Ready** lane only
3. Move card to **In Progress**, comment with plan
4. Create a feature branch: `feature/<short-name>`

## Spec Discipline

- Non-trivial work requires a spec in `.agents/specs/` before starting
- Use [[specs/TEMPLATE]] as the starting point
- Link the spec from the card description

## During Work

- Comment on the card with progress (write for a reader with no context)
- Move card to **Review** when PR is open
- Move card to **Done** when merged

## Verification Checklist

Every card must pass all of these before moving to Review:

1. `dotnet build` — 0 errors, 0 warnings
2. `dotnet format --verify-no-changes` — clean
3. `dotnet test` — all pass (includes Aspire smoke tests in `Collabhost.AppHost.Tests`)

Note: The Aspire smoke tests (`Collabhost.AppHost.Tests`) boot the real AppHost and test against real Kestrel endpoints. They replace the previous manual `aspire start` + curl workflow.

## PR Review Resolution

When resolving PR review comments (dispatched agent or direct work):

### Rules for the resolving agent

1. **Read all PR comments first** before making any changes
2. **Comment back on the PR** when appropriate — acknowledge, discuss, push back with reasoning
3. **Use agency** — if a comment seems wrong or has tradeoffs, reply explaining why and propose alternatives rather than blindly accepting
4. **Group related changes** — address all comments in a single pass, not one at a time
5. **Re-run the full verification checklist** after all changes

### Reporting back to coordinator

The resolving agent MUST return a structured report that includes:

1. **Comments resolved** — what was changed and why
2. **Comments pushed back on** — what was challenged and the reasoning
3. **New patterns discovered** — any new conventions, patterns, or architectural decisions that emerged from the review feedback. **This is critical** — the coordinator MUST discuss these with the user before they become conventions.
4. **Verification results** — build/format/test status after changes

### Coordinator responsibilities

- **Surface new patterns** to the user for discussion before codifying
- **Update project docs** (CLAUDE.md, ARCHITECTURE.md, conventions) with any agreed-upon patterns
- **Do not auto-merge** — wait for user approval after review resolution

## Commit Discipline

- **Commit everything.** When work is done, `git status` should be clean. Docs, CLAUDE.md, .gitignore, config — everything gets committed with the current work. Never leave changes uncommitted "for later" or "for the next agent."
- **No orphaned changes.** If you update CLAUDE.md, a spec, or any file during a card, it goes in that card's commit. There is no reason to defer.

## Cleanup

- Archive completed specs to `.agents/archive/specs/`
- Post-mortems go to `.agents/archive/postmortems/`
- Sweep `.agents/temp/` regularly — nothing persists there
