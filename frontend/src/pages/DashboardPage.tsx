import { ActionButton } from '@/actions/ActionButton'
import type { AppListItem } from '@/api/types'
import { useApps, useStartApp, useStopApp } from '@/hooks/use-apps'
import { useDashboardEvents, useDashboardStats } from '@/hooks/use-dashboard'
import { formatMemory } from '@/lib/format'
import { ROUTES } from '@/lib/routes'
import { EmptyState } from '@/shared/EmptyState'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { EventList } from '@/shared/EventList'
import { SectionDivider } from '@/shared/SectionDivider'
import { Spinner } from '@/shared/Spinner'
import { TypeBadge } from '@/shared/TypeBadge'
import { StatusDot } from '@/status/StatusDot'
import { StatusStrip } from '@/status/StatusStrip'
import { StatusText } from '@/status/StatusText'
import type { Column } from '@/tables/DataTable'
import { DataTable } from '@/tables/DataTable'
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
          color: 'amber' as const,
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
          color: 'amber' as const,
        },
      ]
    : []

  const columns: Column<AppListItem>[] = [
    {
      key: 'status-dot',
      header: '',
      width: '28px',
      render: (app) => <StatusDot status={app.status} />,
    },
    {
      key: 'name',
      header: 'Name',
      sortFn: (a, b) => a.displayName.localeCompare(b.displayName),
      render: (app) => (
        <div>
          <div className="text-xs" style={{ color: 'var(--wm-text-bright)', fontWeight: 600 }}>
            {app.displayName}
          </div>
          <div className="text-xs" style={{ color: 'var(--wm-text-dim)' }}>
            {app.name}
          </div>
        </div>
      ),
    },
    {
      key: 'type',
      header: 'Type',
      render: (app) => <TypeBadge label={app.appType.name} />,
    },
    {
      key: 'status',
      header: 'Status',
      render: (app) => <StatusText status={app.status} />,
    },
    {
      key: 'domain',
      header: 'Domain',
      render: (app) => (
        <span
          className="text-xs"
          style={{
            color: app.domainActive ? 'var(--wm-text-dim)' : 'var(--wm-text-dim)',
            opacity: app.domainActive ? 1 : 0.5,
          }}
        >
          {app.domain ?? '--'}
        </span>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      align: 'right',
      render: (app) => {
        const isAnyPending = startMutation.isPending || stopMutation.isPending
        return (
          // biome-ignore lint/a11y/useKeyWithClickEvents: stopPropagation prevents row navigation, buttons have own handlers
          <div className="flex items-center gap-1 justify-end" onClick={(e) => e.stopPropagation()}>
            {app.actions.canStart && (
              <ActionButton
                variant="success"
                size="sm"
                disabled={isAnyPending}
                onClick={() => startMutation.mutate(app.name)}
              >
                Start
              </ActionButton>
            )}
            {app.actions.canStop && (
              <ActionButton
                variant="default"
                size="sm"
                disabled={isAnyPending}
                onClick={() => stopMutation.mutate(app.name)}
              >
                Stop
              </ActionButton>
            )}
          </div>
        )
      },
    },
  ]

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
