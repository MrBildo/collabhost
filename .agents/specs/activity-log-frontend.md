# Activity Log -- Frontend Spec

> Card #103. Companion to Remy's backend spec (`activity-log.md`).
> Author: Dana, 2026-04-07.

---

## 1. Current State Audit

### What Exists

The full frontend pipeline was built during the dashboard phase and is still intact. I hid it from the dashboard during the UI polish release because the backend was returning 404.

| File | What | Status |
|------|------|--------|
| `api/types.ts` | `DashboardEventsResponse`, `DashboardEvent` | Exists, matches backend contract |
| `api/endpoints.ts` | `getDashboardEvents(limit?)` | Exists, calls `GET /api/v1/dashboard/events` |
| `hooks/use-dashboard.ts` | `useDashboardEvents(limit)` | Exists, polls at `POLL_INTERVALS.dashboard` (5s) |
| `shared/EventList.tsx` | Event list component with severity colors | Exists, renders `DashboardEvent[]` |
| `pages/DashboardPage.tsx` | Dashboard page | Exists, does NOT render EventList (removed during UI polish) |
| `lib/format.ts` | `formatTimestamp(iso)` | Exists, used by EventList |

### What Works

Everything except the actual data flow. The components render correctly with mock data. The hook, endpoint, and types are wired. The pipeline just needs:

1. The backend endpoint to stop returning 404
2. The `EventList` to be rendered on the DashboardPage again

### What Needs Changes

**No type changes needed for Phase 1 (dashboard integration).** The `DashboardEventsResponse` and `DashboardEvent` types already match the backend contract exactly. Remy confirmed this in Section 11 of the backend spec.

**Phase 2 (full Activity Log page)** will need new types, a new hook, a new endpoint function, new components, and a new route.

---

## 2. Dashboard Integration (Phase 1)

### DashboardPage Changes

Restore the `EventList` rendering that was removed during UI polish. This is a small, mechanical change.

```tsx
// In DashboardPage.tsx:
// 1. Add import for useDashboardEvents
// 2. Add import for EventList
// 3. Add import for SectionDivider (already imported)
// 4. Call useDashboardEvents() hook
// 5. Render EventList below the apps table

// After the DataTable or EmptyState:
<SectionDivider label="Recent Activity" className="mb-3 mt-5" />
{eventsQuery.data ? (
  <EventList events={eventsQuery.data.events} />
) : eventsQuery.isLoading ? (
  <Spinner />
) : null}
```

The `useDashboardEvents` hook already handles error state by stopping polling on error (the `refetchInterval` callback returns `false` on error). No error banner for events -- the dashboard should degrade gracefully if the events endpoint fails. The stats and apps table are the primary content; events are supplementary.

### Polling Interval

Currently `POLL_INTERVALS.dashboard` is 5 seconds. The events endpoint shares this interval. This is appropriate for a dashboard that needs to feel alive. The backend has no caching (per Kai's feedback, Remy dropped the 30s TTL cache), so every poll hits SQLite directly. At `LIMIT 20 ORDER BY Id DESC` on the primary key, this is sub-millisecond -- no concern.

### No Changes to Existing Files

- `api/types.ts` -- no changes
- `api/endpoints.ts` -- no changes
- `hooks/use-dashboard.ts` -- no changes
- `shared/EventList.tsx` -- no changes
- `lib/format.ts` -- no changes

The only file that changes for Phase 1 is `pages/DashboardPage.tsx`.

---

## 3. Recommendation: `appName` vs `appSlug`

**Recommendation: Keep as slug. Rename the field to `appSlug` on both sides.**

Here is my reasoning:

1. **Slugs are what the operator sees everywhere else.** The app list table, the URL bar, the detail page header, the route domain -- slugs are the primary identifier throughout the system. Switching the events feed to display names would be inconsistent with every other surface.

2. **Slugs are linkable.** When I build the full Activity Log page in Phase 2, clicking an app name in an event should navigate to `/apps/{slug}`. If the field contains a display name, I need a separate slug field to build the link. If the field IS the slug, the link is trivial.

3. **Display names add denormalization cost for no UX gain.** Adding `AppDisplayName` to the `ActivityEvent` entity means every event write carries both slug and display name. Display names can be long ("My Production API Server v2") and would break the compact event row layout. Slugs are short and monospace-friendly -- they fit the War Machine aesthetic.

4. **The field name `appName` is the only problem.** The value (slug) is correct. The name is misleading. Fix the name, not the value.

**Proposed change:**

- Frontend: Rename `DashboardEvent.appName` to `DashboardEvent.appSlug`
- Backend: Rename the JSON property from `appName` to `appSlug` in the `DashboardEventResponse` record

This is a coordinated rename. Since the backend endpoint doesn't exist yet (it's being built now), there is no migration concern. The frontend `DashboardEvent` type and `EventList` component reference `event.appName` -- rename to `event.appSlug`. Two files, three occurrences.

If Bill prefers to keep `appName` as-is to avoid the rename, that's also fine -- the value is correct and the field name is harmless. But if we're deciding now before the backend ships, rename it.

---

## 4. Phase 2: Activity Log Page (Future Scope)

This section specs the dedicated Activity Log page that uses the full `GET /api/v1/events` endpoint. Not part of the initial card, but spec'd here for completeness so the backend contract is validated.

### 4.1 New Types

Add to `api/types.ts`:

```typescript
type ActivityEvent = {
  id: string
  eventType: string
  actorId: string
  actorName: string
  appId: string | null
  appSlug: string | null
  metadata: Record<string, unknown> | null
  timestamp: string
  severity: 'info' | 'warning' | 'error'
}

type ActivityEventListResponse = {
  items: ActivityEvent[]
  nextCursor: string | null
  hasMore: boolean
}

type ActivityEventCategory = 'app' | 'user' | 'proxy'

type ActivityEventSeverity = 'info' | 'warning' | 'error'
```

Note: `severity` is derived server-side via `DeriveSeverity` and included in each `ActivityEventItem`. The frontend does not need to derive it -- it's a display value from the backend.

### 4.2 New Endpoint Function

Add to `api/endpoints.ts`:

```typescript
type ActivityEventQueryParams = {
  category?: string
  appSlug?: string
  actorId?: string
  eventType?: string
  since?: string
  until?: string
  limit?: number
  cursor?: string
}

function getActivityEvents(params?: ActivityEventQueryParams): Promise<ActivityEventListResponse> {
  const searchParams = new URLSearchParams()
  if (params?.category) searchParams.set('category', params.category)
  if (params?.appSlug) searchParams.set('appSlug', params.appSlug)
  if (params?.actorId) searchParams.set('actorId', params.actorId)
  if (params?.eventType) searchParams.set('eventType', params.eventType)
  if (params?.since) searchParams.set('since', params.since)
  if (params?.until) searchParams.set('until', params.until)
  if (params?.limit) searchParams.set('limit', String(params.limit))
  if (params?.cursor) searchParams.set('cursor', params.cursor)
  const qs = searchParams.toString()
  return request(`/events${qs ? `?${qs}` : ''}`)
}
```

### 4.3 New Hook

New file: `hooks/use-activity-events.ts`

```typescript
function useActivityEvents(params?: ActivityEventQueryParams) {
  return useQuery<ActivityEventListResponse>({
    queryKey: ['activity-events', params],
    queryFn: () => getActivityEvents(params),
    // No polling -- the activity log page is a search/filter interface, not a live feed.
    // The operator refreshes manually or adjusts filters.
  })
}
```

**No polling on this hook.** The full activity log is a search interface, not a live dashboard feed. The operator sets filters, reviews results, and pages through history. Auto-refresh would fight against filter state and cursor position. If real-time updates are ever needed, that's SSE territory (deferred feature).

### 4.4 New Components

#### ActivityLogPage

New file: `pages/ActivityLogPage.tsx`

The page layout:

```
// System Overview header pattern (consistent with other pages)
// Filter bar (chips for category, app, severity; text input for event type)
// Event table (full-width, monospace-dense)
// Load more button (keyset pagination)
```

**Filter bar:** Horizontal row of filter controls. Category chips (`All`, `App`, `User`, `Proxy`), severity chips (`All`, `Info`, `Warning`, `Error`), app slug text filter, event type dropdown (populated from known event types). Filters update the `params` object passed to `useActivityEvents`. Changing any filter resets the cursor.

**Event table columns:**

| Column | Source | Width | Notes |
|--------|--------|-------|-------|
| Timestamp | `event.timestamp` | Fixed | `formatDateTime` -- full date+time, not just time-of-day like dashboard |
| Event | `event.eventType` | Fixed | Format with `formatEventType` (new) -- e.g. "App Started", "User Created" |
| App | `event.appSlug` | Auto | Clickable link to `/apps/{slug}`. Dim "---" for null (non-app events) |
| Actor | `event.actorName` | Fixed | "Admin", "System", "DevBot" |
| Details | `event.metadata` | Auto | Render metadata inline. "exit code 1", "process, routing", etc. |
| Severity | `event.severity` | Fixed | Dot indicator matching EventList severity colors |

**Pagination:** "Load more" button at the bottom. Uses `nextCursor` from the response. Appends new items to the existing list (client-side accumulation). The `hasMore` flag controls button visibility.

**No DataTable reuse.** The activity log table has different interaction patterns than the app list table (no row click navigation, no inline actions, metadata column with variable content). A purpose-built table component is more appropriate than forcing DataTable to accommodate it.

#### ActivityEventRow

The row component for the activity log table. Receives a single `ActivityEvent` and renders it.

#### ActivityLogFilterBar

The filter bar component. Manages its own local state for the active filters and calls `onFilterChange` when filters change. The parent page holds the canonical filter state and passes it to the hook.

### 4.5 New Format Functions

Add to `lib/format.ts`:

```typescript
const EVENT_TYPE_LABELS: Record<string, string> = {
  'app.started': 'App Started',
  'app.stopped': 'App Stopped',
  'app.restarted': 'App Restarted',
  'app.killed': 'App Killed',
  'app.created': 'App Created',
  'app.deleted': 'App Deleted',
  'app.settings_updated': 'Settings Updated',
  'app.crashed': 'App Crashed',
  'app.fatal': 'App Fatal',
  'app.auto_started': 'Auto Started',
  'app.auto_restarted': 'Auto Restarted',
  'app.seeded': 'App Seeded',
  'proxy.reloaded': 'Proxy Reloaded',
  'user.created': 'User Created',
  'user.deactivated': 'User Deactivated',
  'user.seeded': 'User Seeded',
}

function formatEventType(eventType: string): string {
  return EVENT_TYPE_LABELS[eventType] ?? eventType
}

function formatEventMetadata(metadata: Record<string, unknown> | null): string | null {
  if (!metadata) return null
  const parts: string[] = []
  if ('exitCode' in metadata) parts.push(`exit code ${metadata.exitCode}`)
  if ('failureCount' in metadata) parts.push(`${metadata.failureCount} failures`)
  if ('changedCapabilities' in metadata && Array.isArray(metadata.changedCapabilities)) {
    parts.push(metadata.changedCapabilities.join(', '))
  }
  if ('targetName' in metadata) parts.push(String(metadata.targetName))
  if ('role' in metadata) parts.push(String(metadata.role))
  if ('restartCount' in metadata) parts.push(`restart #${metadata.restartCount}`)
  return parts.length > 0 ? parts.join(' -- ') : null
}
```

These are display formatting functions. The backend sends machine identifiers; the frontend formats them for humans. Standard pattern.

### 4.6 Routing

Add to `lib/routes.ts`:

```typescript
activityLog: '/activity',
```

Add to `app.tsx`:

```tsx
<Route path="activity" element={<ActivityLogPage />} />
```

Add "Activity" to the topbar navigation in `chrome/Topbar.tsx`.

### 4.7 Implementation Notes

- The ActivityLogPage is a standalone feature with no dependencies on the dashboard EventList. They share the same backend data source but use different endpoints and different types.
- The keyset pagination pattern (cursor-based "load more") is new to the frontend. No existing component does this. The pattern: accumulate items in local state, append new page results, track the current cursor.
- The filter bar should use URL search params (`useSearchParams`) so filter state is shareable via URL. An operator should be able to link to "show me all crashes for my-api" and have the filter pre-populated.

---

## 5. Feedback on Backend Spec

### 5.1 The `message` field in `DashboardEventResponse`

Remy's spec (Section 8.1) says the `message` field is a human-readable summary derived from `EventType` and metadata. Examples: "started", "crashed (exit code 1)", "settings updated (process, routing)".

This is good. The backend is formatting the message, not just passing through the event type. The frontend `EventList` component renders `event.message` directly -- it does not need to know about event types or metadata shapes. Clean separation.

One request: for app events, the message should NOT include the app name/slug. The `EventList` component already renders `event.appSlug` (currently `event.appName`) as a bold prefix before the message. If the message also contains the app name, it will appear twice. The backend spec's examples ("started", "crashed (exit code 1)") look correct -- they omit the app name. Just calling this out explicitly so an implementing agent doesn't add it.

### 5.2 The `source` field mapping

The spec maps `source` to `ActorName`. This is correct for the EventList rendering -- the source badge shows "Admin", "System", "DevBot". No concerns.

### 5.3 Dashboard events polling interval vs cache

With the cache dropped (per Kai's feedback), every 5s poll hits SQLite. This is fine for correctness, but it means there is zero staleness -- an operator starts an app and sees the event on the next poll cycle (at most 5 seconds). Good UX.

### 5.4 No concerns about the full events endpoint

The `GET /api/v1/events` contract (Section 8.2-8.3) is clean. `ActivityEventItem` includes `severity` as a derived field, which is exactly right -- the frontend should not need to know the severity derivation rules. The `Metadata` field as `JsonElement?` will serialize to a plain JSON object or null in the response, which maps directly to `Record<string, unknown> | null` in TypeScript.

The keyset pagination shape (`items`, `nextCursor`, `hasMore`) is a good contract. `hasMore` as an explicit boolean is better than requiring the client to infer "no more pages" from `nextCursor === null` -- both say the same thing, but `hasMore` is unambiguous.

### 5.5 One gap: no event type list endpoint

The full events endpoint accepts `eventType` as a filter parameter, and the Phase 2 frontend will want to show event types in a dropdown. The backend spec defines 16 event types as constants in `_Constants.cs`, but there is no endpoint that serves them as a list. The frontend should not hardcode 16 event type strings.

**Options:**
1. **Backend serves `GET /api/v1/events/types`** -- returns the list of known event types. Clean, follows the "backend is source of truth for option values" pattern from the KB.
2. **Frontend derives from observed data** -- query events, extract unique `eventType` values. Fragile, incomplete (types with no events won't appear), and violates the convention.
3. **Accept hardcoding for Phase 2** -- the event types are stable and defined in the spec. A hardcoded `EVENT_TYPE_LABELS` map (shown in Section 4.5 above) is pragmatic. When a new event type is added, the frontend `formatEventType` falls through to the raw string, which is acceptable degradation.

**My recommendation:** Option 3 for now. The event type list is small, stable, and the fallback behavior (raw string display) is harmless. If the event type catalog grows significantly, add the endpoint then. Over-engineering a lookup endpoint for 16 static strings is not worth it today. This is a judgment call -- I'm comfortable with it because the format function gracefully handles unknown types.

---

## 6. Implementation Phasing

### Phase 1: Dashboard Integration (Size: XS)

**Card scope:** Wire `EventList` into `DashboardPage`.

**Files changed:**
- `pages/DashboardPage.tsx` -- add `useDashboardEvents` hook call, render `SectionDivider` + `EventList` below the apps table

**Dependencies:** Backend Phase 4 (the `GET /api/v1/dashboard/events` endpoint must exist).

**Optional:** If Bill approves the `appName` -> `appSlug` rename, also update:
- `api/types.ts` -- rename `DashboardEvent.appName` to `DashboardEvent.appSlug`
- `shared/EventList.tsx` -- update `event.appName` references to `event.appSlug`

**Verification:** Run the app with Aspire, navigate to dashboard, confirm events render. Start/stop an app, confirm the event appears within 5 seconds.

**Estimated effort:** 15 minutes of actual code changes. Most of the work is verification.

### Phase 2: Activity Log Page (Size: M)

**Card scope:** Full activity log page with filtering and keyset pagination.

**Files created:**
- `pages/ActivityLogPage.tsx`
- `hooks/use-activity-events.ts`

**Files modified:**
- `api/types.ts` -- add `ActivityEvent`, `ActivityEventListResponse`, `ActivityEventCategory`, `ActivityEventSeverity`
- `api/endpoints.ts` -- add `getActivityEvents(params)`
- `lib/format.ts` -- add `formatEventType`, `formatEventMetadata`
- `lib/routes.ts` -- add `activityLog` route
- `app.tsx` -- add `<Route>` for activity log
- `chrome/Topbar.tsx` -- add "Activity" nav link

**Dependencies:** Backend Phase 4 (the `GET /api/v1/events` endpoint must exist).

**This is a separate card.** Phase 2 is a distinct feature with its own scope, its own testing, and its own UX review. It should not be bundled with Phase 1.

### Phase 3: Tests (Size: S)

**Files created:**
- `shared/EventList.test.tsx` -- render tests for empty state, severity colors, event rendering
- `pages/DashboardPage.test.tsx` -- render tests for events section (mock hook data)
- `hooks/use-activity-events.test.ts` -- query key and params tests (Phase 2)
- `pages/ActivityLogPage.test.tsx` -- filter state, pagination, rendering (Phase 2)
- `lib/format.test.ts` -- add tests for `formatEventType`, `formatEventMetadata` (Phase 2)

Phase 1 tests (EventList, DashboardPage) can ship with Phase 1. Phase 2 tests ship with Phase 2.

---

## 7. Risk Assessment

**Low risk overall.** Phase 1 is restoring code I already built and hid. The types match, the pipeline is wired, the component renders. The only risk is if the backend response shape diverges from the existing `DashboardEventsResponse` contract -- but Remy explicitly confirmed it matches in Section 11.

**Phase 2 introduces one new pattern:** keyset pagination with client-side accumulation. This is conceptually simple but needs careful state management -- appending pages, resetting on filter change, tracking the cursor. The hook should own this state, not the page component. Worth getting right the first time because any future paginated list (audit logs, deployment history) will reuse the pattern.

---

## 8. Summary

| Item | Decision |
|------|----------|
| `appName` field | Recommend rename to `appSlug` on both sides. Value stays as slug. |
| Dashboard integration | Restore EventList on DashboardPage. XS effort, zero type changes. |
| Activity Log page | Phase 2, separate card. New types, endpoint, hook, page, format functions. |
| Polling on activity page | No. Filter/search interface, not a live feed. |
| Event type list endpoint | Not needed. Hardcoded format map with raw-string fallback is sufficient. |
| Backend spec gaps | One: message field must not include app name (would duplicate EventList rendering). |

-- Dana
