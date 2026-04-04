import { ActionButton } from '@/actions/ActionButton'
import { useApps, useStartApp, useStopApp } from '@/hooks/use-apps'
import { ROUTES } from '@/lib/routes'
import { EmptyState } from '@/shared/EmptyState'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { Spinner } from '@/shared/Spinner'
import { StatusStrip } from '@/status/StatusStrip'
import { DataTable } from '@/tables/DataTable'
import { FilterBar } from '@/tables/FilterBar'
import type { StatusFilter } from '@/tables/FilterBar'
import { buildAppListColumns } from '@/tables/app-columns'
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

  const columns = buildAppListColumns({
    onStart: (slug) => startMutation.mutate(slug),
    onStop: (slug) => stopMutation.mutate(slug),
    isActionPending: startMutation.isPending || stopMutation.isPending,
  })

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
