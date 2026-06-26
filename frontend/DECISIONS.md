# Frontend Architecture Decisions

This file records *deliberate* frontend architecture decisions — including deliberate
non-goals — together with the reasoning behind them. Its job is narrow: when a settled
decision has no recorded rationale, every capable new reviewer re-flags it in good faith,
and the team pays the same explanation cost over and over. Recording the decision here is
what stops that churn.

A decision earns a place here when it is (a) deliberate, (b) non-obvious enough that a
sharp reviewer would reasonably question it, and (c) settled. Each entry is self-contained:
a reviewer should be able to read it and be satisfied without chasing down a PR, a card, or
a person.

## 1. No runtime schema validation at the API trust boundary (non-goal)

**Decision.** The frontend does not run a runtime schema-validation layer (Zod or
equivalent) over API responses. Responses from `/api/v1/*` are consumed as their declared
TypeScript types, with no runtime re-validation/parse step. There is intentionally no `zod`
dependency.

**Why this is deliberate, not an oversight.** A runtime validation layer at a trust
boundary earns its keep when the data source can drift *independently* of the code that
consumes it — a third-party API, a separately-deployed service, a public webhook, persisted
data written by an older version. None of those apply here. The Collabhost backend and this
frontend are a single, co-versioned unit: the React dashboard ships *inside the same binary*
as the API (the built assets are served from the API's `wwwroot/`), and both are released
together from the same commit. The wire contract therefore cannot drift out from under the
types the frontend was built against — there is no deployment in which a newer API answers
an older frontend, or the reverse. Adding a runtime re-validation layer would guard against
a class of drift this architecture makes impossible, at the standing cost of a parallel
schema kept in lockstep with every endpoint and a parse step on every response.

This is a property of Collabhost's same-binary, co-versioned shipping model — not a general
position on runtime validation. If a future surface consumes data that genuinely *can* drift
independently (an external API, user-uploaded content, persisted state read across versions),
that surface is a real trust boundary and should validate at it. The non-goal is scoped to
the in-house, co-shipped API.

(The `typescript-dev` skill lists Zod at trust boundaries as a general convention — sound in
general, and exactly the convention a sharp reviewer would invoke to question this. It does
not apply here because the boundary it would guard cannot independently drift. That skill
defers to project decisions on conflicts; this file is that decision.)

## 2. Cross-tab auth-state sync (decision-with-history; currently implemented)

**Current state.** `use-auth` *does* synchronize auth state across browser tabs, via a
`storage`-event listener in `src/hooks/use-auth.ts`. A login or logout in one tab propagates
to the others without a reload.

**Why this entry exists.** Cross-tab auth sync was originally a deliberate non-goal:
same-tab login/logout already notifies subscribers directly, and cross-tab propagation was
judged out of scope. That was later reconsidered and **deliberately reversed** — without it,
a logout (or login) in one tab leaves the other tabs sitting on a stale session until the
operator reloads, a real and observable wrong state. The `storage` listener was added to
close that gap (finding FE-AUTH-04, card #434). It is filtered to fire only on changes to
the auth-storage key (a full `clear()` surfaces as a `null` key and is also handled), so
unrelated `localStorage` writes don't churn the auth snapshot.

The live rationale lives inline at the code site (`src/hooks/use-auth.ts`); this entry is
here for discoverability and honest history. A reviewer asking either "why is there a
cross-tab `storage` listener?" or "shouldn't auth sync across tabs?" finds the settled
answer: it was scoped out, then deliberately adopted, and the listener is the current
intended behavior.

## Adding to this file

When you make a deliberate frontend architecture decision — especially a *non-goal*, a thing
the codebase intentionally does **not** do — that a future reviewer would reasonably
question, record it here with self-contained rationale. The bar is "deliberate, non-obvious,
and settled," not "every choice." Keep each entry tight: state the decision, then the
reasoning a reviewer needs to be satisfied without leaving this file. If a decision later
reverses (as #2 did), re-cast the entry as decision-with-history rather than deleting it —
the recorded history is what keeps the question from being re-opened.
