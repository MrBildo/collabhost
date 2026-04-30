import { ActionButton } from '@/actions/ActionButton'
import type { RouteEntry } from '@/api/types'
import { useReloadProxy, useRoutes } from '@/hooks/use-routes'
import { ROUTES } from '@/lib/routes'
import { EmptyState } from '@/shared/EmptyState'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { Spinner } from '@/shared/Spinner'
import { StatusDot } from '@/status/StatusDot'
import type { Column } from '@/tables/DataTable'
import { DataTable } from '@/tables/DataTable'
import { useNavigate } from 'react-router-dom'

function RoutesPage() {
  const navigate = useNavigate()
  const routesQuery = useRoutes()
  const reloadMutation = useReloadProxy()

  const routes = routesQuery.data?.routes ?? []
  const baseDomain = routesQuery.data?.baseDomain ?? ''

  const modeLabels: Record<string, string> = {
    reverseproxy: 'Reverse Proxy',
    fileserver: 'File Server',
  }

  const columns: Column<RouteEntry>[] = [
    {
      key: 'status',
      header: '',
      width: '32px',
      render: (route) => <StatusDot status={route.enabled ? 'running' : 'stopped'} />,
    },
    {
      key: 'domain',
      header: 'Domain',
      sortFn: (a, b) => a.domain.localeCompare(b.domain),
      render: (route) =>
        route.enabled ? (
          <a
            href={`${route.https ? 'https' : 'http'}://${route.domain}`}
            target="_blank"
            rel="noopener noreferrer"
            className="text-xs"
            style={{ color: 'var(--wm-text-bright)', fontWeight: 600, textDecoration: 'none' }}
            onMouseEnter={(e) => {
              e.currentTarget.style.color = 'var(--wm-amber)'
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.color = 'var(--wm-text-bright)'
            }}
            onClick={(e) => e.stopPropagation()}
          >
            {route.domain}
          </a>
        ) : (
          <span className="text-xs" style={{ color: 'var(--wm-text-dim)' }}>
            {route.domain}
          </span>
        ),
    },
    {
      key: 'target',
      header: 'Target',
      render: (route) => (
        <span className="text-xs" style={{ color: 'var(--wm-text-dim)', fontVariantNumeric: 'tabular-nums' }}>
          {route.target}
        </span>
      ),
    },
    {
      key: 'proxyMode',
      header: 'Mode',
      render: (route) => (
        <span className="text-xs" style={{ color: 'var(--wm-text-dim)' }}>
          {route.enabled ? (modeLabels[route.proxyMode.toLowerCase()] ?? route.proxyMode) : 'Disabled'}
        </span>
      ),
    },
    {
      key: 'https',
      header: 'HTTPS',
      width: '60px',
      render: (route) => (
        <span className="text-xs" style={{ color: route.https ? 'var(--wm-green)' : 'var(--wm-text-dim)' }}>
          {route.https ? 'Yes' : 'No'}
        </span>
      ),
    },
    {
      key: 'app',
      header: 'App',
      render: (route) => (
        <button
          type="button"
          className="text-xs wm-link"
          style={{ background: 'none', border: 'none', cursor: 'pointer', padding: 0, color: 'var(--wm-amber)' }}
          onClick={(e) => {
            e.stopPropagation()
            navigate(ROUTES.appDetail(route.appName))
          }}
        >
          {route.appDisplayName}
        </button>
      ),
    },
  ]

  if (routesQuery.isLoading) {
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
          <span style={{ color: 'var(--wm-text-dim)' }}>{'// '}</span>Routes
        </h1>
        <ActionButton size="sm" onClick={() => reloadMutation.mutate()} disabled={reloadMutation.isPending}>
          {reloadMutation.isPending ? 'Reloading...' : 'Reload Proxy'}
        </ActionButton>
      </div>

      {routesQuery.error && (
        <ErrorBanner
          message={routesQuery.error instanceof Error ? routesQuery.error.message : 'Failed to load routes'}
          className="mb-4"
        />
      )}

      {reloadMutation.isError && (
        <ErrorBanner
          message={reloadMutation.error instanceof Error ? reloadMutation.error.message : 'Failed to reload proxy'}
          className="mb-4"
        />
      )}

      {reloadMutation.isSuccess && (
        <div className="wm-alert mb-4" style={{ borderColor: 'var(--wm-green)', color: 'var(--wm-green)' }}>
          Proxy configuration reloaded successfully.
        </div>
      )}

      {baseDomain && (
        <div className="mb-3 text-xs" style={{ color: 'var(--wm-text-dim)' }}>
          Base domain: <span style={{ color: 'var(--wm-text-bright)' }}>{baseDomain}</span>
        </div>
      )}

      {routes.length === 0 ? (
        <EmptyState
          title="No routes configured"
          description="Routes are created automatically when apps with domains are registered."
        />
      ) : (
        <DataTable
          columns={columns}
          data={routes}
          keyFn={(route) => route.appExternalId}
          rowClassName={(route) => (!route.enabled ? 'wm-table-row--disabled' : undefined)}
        />
      )}
    </div>
  )
}

export { RoutesPage }
