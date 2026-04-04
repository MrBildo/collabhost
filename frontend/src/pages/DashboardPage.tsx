import { ActionButton } from '@/actions/ActionButton'
import { useApps, useStartApp, useStopApp } from '@/hooks/use-apps'
import { useDashboardEvents, useDashboardStats } from '@/hooks/use-dashboard'
import { formatMemory } from '@/lib/format'
import { ROUTES } from '@/lib/routes'
import { EmptyState } from '@/shared/EmptyState'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { EventList } from '@/shared/EventList'
import { SectionDivider } from '@/shared/SectionDivider'
import { Spinner } from '@/shared/Spinner'
import { StatusStrip } from '@/status/StatusStrip'
import { DataTable } from '@/tables/DataTable'
import { buildDashboardColumns } from '@/tables/app-columns'
import { Link, useNavigate } from 'react-router-dom'

function DashboardPage() {
  const navigate = useNavigate()
  const statsQuery = useDashboardStats()
  const eventsQuery = useDashboardEvents()
  const appsQuery = useApps()
  const startMutation = useStartApp()
  const stopMutation = useStopApp()

  const stats = statsQuery.data
  const events = eventsQuery.data?.events ?? []
  const apps = appsQuery.data ?? []

  const isLoading = statsQuery.isLoading || appsQuery.isLoading
  const error = statsQuery.error || appsQuery.error

  const statusCells = stats
    ? [
        { label: 'Total Apps', value: stats.totalApps, detail: `${stats.appTypes} app types`, color: 'amber' as const },
        { label: 'Running', value: stats.running, color: 'green' as const },
        {
          label: 'Issues',
          value: stats.issues,
          detail: stats.issuesSummary ?? undefined,
          color: stats.issues > 0 ? ('red' as const) : ('default' as const),
        },
        {
          label: '24h Uptime',
          value: stats.uptimePercent24h != null ? `${stats.uptimePercent24h}%` : '--',
          detail: `${stats.incidentsThisWeek} incidents this week`,
          color: stats.uptimePercent24h != null ? ('amber' as const) : ('default' as const),
        },
        {
          label: 'Memory',
          value: formatMemory(stats.memoryUsedMb),
          detail: stats.memoryTotalMb != null ? `of ${formatMemory(stats.memoryTotalMb)}` : undefined,
        },
        {
          label: 'Req/min',
          value: stats.requestsPerMinute ?? '--',
          detail: 'across all apps',
          color: stats.requestsPerMinute != null ? ('amber' as const) : ('default' as const),
        },
      ]
    : []

  const columns = buildDashboardColumns({
    onStart: (slug) => startMutation.mutate(slug),
    onStop: (slug) => stopMutation.mutate(slug),
    isActionPending: startMutation.isPending || stopMutation.isPending,
  })

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
        <span className="text-xs" style={{ color: 'var(--wm-text-dim)' }}>
          auto-refresh 10s
        </span>
      </div>

      {error && (
        <ErrorBanner message={error instanceof Error ? error.message : 'Failed to load dashboard'} className="mb-4" />
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

      <SectionDivider label="Recent Events" className="mb-3" />

      {eventsQuery.isLoading ? <Spinner /> : <EventList events={events} />}
    </div>
  )
}

export { DashboardPage }
