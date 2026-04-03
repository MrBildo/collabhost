import { ActionButton } from '@/actions/ActionButton'
import type { AppListItem } from '@/api/types'
import { useApps, useStartApp, useStopApp } from '@/hooks/use-apps'
import { formatUptime } from '@/lib/format'
import { ROUTES } from '@/lib/routes'
import { EmptyState } from '@/shared/EmptyState'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { Spinner } from '@/shared/Spinner'
import { TypeBadge } from '@/shared/TypeBadge'
import { StatusDot } from '@/status/StatusDot'
import { StatusStrip } from '@/status/StatusStrip'
import { StatusText } from '@/status/StatusText'
import type { Column } from '@/tables/DataTable'
import { DataTable } from '@/tables/DataTable'
import { FilterBar } from '@/tables/FilterBar'
import type { StatusFilter } from '@/tables/FilterBar'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router-dom'

function AppListPage() {
  const navigate = useNavigate()
  const appsQuery = useApps()
  const startMutation = useStartApp()
  const stopMutation = useStopApp()

  const [statusFilter, setStatusFilter] = useState<StatusFilter>('all')
  const [searchTerm, setSearchTerm] = useState('')

  const apps = appsQuery.data ?? []

  const filteredApps = useMemo(() => {
    let result = apps
    if (statusFilter !== 'all') {
      result = result.filter((app) => app.status === statusFilter)
    }
    if (searchTerm) {
      const term = searchTerm.toLowerCase()
      result = result.filter(
        (app) =>
          app.name.toLowerCase().includes(term) ||
          app.displayName.toLowerCase().includes(term) ||
          app.appType.displayName.toLowerCase().includes(term),
      )
    }
    return result
  }, [apps, statusFilter, searchTerm])

  const counts = useMemo(() => {
    const running = apps.filter((a) => a.status === 'running').length
    const stopped = apps.filter((a) => a.status === 'stopped').length
    const crashed = apps.filter((a) => a.status === 'crashed').length
    return { total: apps.length, running, stopped, crashed }
  }, [apps])

  const statusCells = [
    { label: 'Total', value: counts.total, color: 'amber' as const },
    { label: 'Running', value: counts.running, color: 'green' as const },
    { label: 'Stopped', value: counts.stopped },
    { label: 'Crashed', value: counts.crashed, color: counts.crashed > 0 ? ('red' as const) : ('default' as const) },
  ]

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
      key: 'port',
      header: 'Port',
      render: (app) => (
        <span className="text-xs" style={{ color: 'var(--wm-text-dim)', fontVariantNumeric: 'tabular-nums' }}>
          {app.port ?? '--'}
        </span>
      ),
    },
    {
      key: 'uptime',
      header: 'Uptime',
      render: (app) => (
        <span className="text-xs" style={{ color: 'var(--wm-text-dim)', fontVariantNumeric: 'tabular-nums' }}>
          {formatUptime(app.uptimeSeconds)}
        </span>
      ),
    },
    {
      key: 'domain',
      header: 'Domain',
      render: (app) => (
        <span className="text-xs" style={{ color: 'var(--wm-text-dim)', opacity: app.domainActive ? 1 : 0.5 }}>
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

  if (appsQuery.isLoading) {
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
          <span style={{ color: 'var(--wm-text-dim)' }}>{'// '}</span>Applications
        </h1>
        <ActionButton variant="amber" size="sm" onClick={() => navigate(ROUTES.appCreate)}>
          Add App
        </ActionButton>
      </div>

      {appsQuery.error && (
        <ErrorBanner
          message={appsQuery.error instanceof Error ? appsQuery.error.message : 'Failed to load apps'}
          className="mb-4"
        />
      )}

      <StatusStrip cells={statusCells} className="mb-4" />

      <FilterBar
        activeFilter={statusFilter}
        onFilterChange={setStatusFilter}
        searchTerm={searchTerm}
        onSearchChange={setSearchTerm}
        className="mb-3"
      />

      {filteredApps.length === 0 ? (
        apps.length === 0 ? (
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
          <EmptyState title="No matching apps" description="Try adjusting your filters." />
        )
      ) : (
        <DataTable
          columns={columns}
          data={filteredApps}
          keyFn={(app) => app.id}
          onRowClick={(app) => navigate(ROUTES.appDetail(app.name))}
        />
      )}
    </div>
  )
}

export { AppListPage }
