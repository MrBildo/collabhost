import { ActionButton } from '@/actions/ActionButton'
import { useApps, useStartApp, useStopApp } from '@/hooks/use-apps'
import { useDashboardEvents, useDashboardStats } from '@/hooks/use-dashboard'
import { useSystemStatus } from '@/hooks/use-system-status'
import { POLL_INTERVALS } from '@/lib/constants'
import { formatActionError } from '@/lib/format-action-error'
import { ROUTES } from '@/lib/routes'
import { EmptyState } from '@/shared/EmptyState'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { EventList } from '@/shared/EventList'
import { SectionDivider } from '@/shared/SectionDivider'
import { Spinner } from '@/shared/Spinner'
import type { StatusCell } from '@/status/StatusStrip'
import { StatusStrip } from '@/status/StatusStrip'
import { buildProxyStateCell } from '@/status/proxyStateCell'
import { DataTable } from '@/tables/DataTable'
import { buildDashboardColumns } from '@/tables/app-columns'
import type { ReactNode } from 'react'
import { Link, useNavigate } from 'react-router-dom'

function DashboardPage() {
  const navigate = useNavigate()
  const statsQuery = useDashboardStats()
  const appsQuery = useApps()
  const eventsQuery = useDashboardEvents()
  const systemStatusQuery = useSystemStatus({ refetchInterval: POLL_INTERVALS.dashboard })
  const startMutation = useStartApp()
  const stopMutation = useStopApp()

  const stats = statsQuery.data
  const apps = appsQuery.data ?? []
  const systemStatus = systemStatusQuery.data

  const isLoading = statsQuery.isLoading || appsQuery.isLoading
  const error = statsQuery.error || appsQuery.error

  const statusCells: StatusCell[] = stats
    ? [
        ...(systemStatus ? [buildProxyStateCell(systemStatus.proxyState)] : []),
        { label: 'Total Apps', value: stats.totalApps, detail: `${stats.appTypes} app types`, color: 'amber' as const },
        { label: 'Running', value: stats.running, color: 'green' as const },
        {
          label: 'Issues',
          value: stats.issues,
          detail: stats.issuesSummary ?? undefined,
          color: stats.issues > 0 ? ('red' as const) : ('default' as const),
        },
      ]
    : []

  // The slug whose start/stop is currently in flight (FE-UI-08) — `variables`
  // holds the arg of the active mutate() call. Only that row disables.
  const pendingSlug = startMutation.isPending
    ? (startMutation.variables ?? null)
    : stopMutation.isPending
      ? (stopMutation.variables ?? null)
      : null

  const columns = buildDashboardColumns({
    onStart: (slug) => startMutation.mutate(slug),
    onStop: (slug) => stopMutation.mutate(slug),
    pendingSlug,
  })

  const actionErrorEntry = startMutation.isError
    ? { verb: 'Start', error: startMutation.error, reset: startMutation.reset }
    : stopMutation.isError
      ? { verb: 'Stop', error: stopMutation.error, reset: stopMutation.reset }
      : null

  // Events feed (FE-QRY-01). Prefer the last-known feed even while a refetch is
  // erroring (TanStack keeps `data` on a stale-then-error query) — the poll
  // backs off but never goes dark. Only when there is no feed at all do we
  // surface the error instead of rendering nothing.
  let eventsSection: ReactNode
  if (eventsQuery.data) {
    eventsSection = <EventList events={eventsQuery.data.events} />
  } else if (eventsQuery.isLoading) {
    eventsSection = <Spinner />
  } else if (eventsQuery.isError) {
    eventsSection = (
      <ErrorBanner
        message={
          eventsQuery.error instanceof Error
            ? `Failed to load recent activity: ${eventsQuery.error.message}`
            : 'Failed to load recent activity'
        }
      />
    )
  } else {
    eventsSection = null
  }

  if (isLoading) {
    return (
      <div className="py-8">
        <Spinner />
      </div>
    )
  }

  return (
    <div>
      <div
        className="flex items-baseline justify-between mb-5 pb-3"
        style={{ borderBottom: '1px solid var(--wm-border)' }}
      >
        <h1 className="wm-section-title" style={{ borderBottom: 'none', paddingBottom: 0 }}>
          <span style={{ color: 'var(--wm-text-dim)' }}>{'// '}</span>System Overview
        </h1>
      </div>

      {error && (
        <ErrorBanner message={error instanceof Error ? error.message : 'Failed to load dashboard'} className="mb-4" />
      )}

      {actionErrorEntry && (
        <ErrorBanner
          message={formatActionError(actionErrorEntry.error, actionErrorEntry.verb)}
          onDismiss={() => actionErrorEntry.reset()}
          className="mb-4"
        />
      )}

      {stats && <StatusStrip cells={statusCells} className="mb-5" />}

      <SectionDivider
        label="Apps"
        action={
          <Link to={ROUTES.apps} className="text-xs" style={{ color: 'var(--wm-text-dim)', textDecoration: 'none' }}>
            View all &rarr;
          </Link>
        }
        className="mb-3"
      />

      {apps.length === 0 ? (
        <EmptyState
          title="No apps registered"
          description="Register your first app to get started."
          action={
            <ActionButton variant="amber" onClick={() => navigate(ROUTES.appCreate)}>
              Add App
            </ActionButton>
          }
        />
      ) : (
        <DataTable
          columns={columns}
          data={apps}
          keyFn={(app) => app.id}
          onRowClick={(app) => navigate(ROUTES.appDetail(app.name))}
          className="mb-5"
        />
      )}

      <SectionDivider label="Recent Activity" className="mb-3 mt-5" />
      {eventsSection}
    </div>
  )
}

export { DashboardPage }
