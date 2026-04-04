import { ActionBar } from '@/actions/ActionBar'
import { ActionButton } from '@/actions/ActionButton'
import { Breadcrumbs } from '@/chrome/Breadcrumbs'
import {
  useAppDetail,
  useAppLogs,
  useDetailKillApp,
  useDetailRestartApp,
  useDetailStartApp,
  useDetailStopApp,
} from '@/hooks/use-app-detail'
import { formatDateTime, formatHealthStatus, formatMemory, formatUptime } from '@/lib/format'
import { ROUTES } from '@/lib/routes'
import type { LogStream } from '@/log/LogViewer'
import { LogViewer } from '@/log/LogViewer'
import { DetailCard } from '@/shared/DetailCard'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { SectionDivider } from '@/shared/SectionDivider'
import { Spinner } from '@/shared/Spinner'
import { TypeBadge } from '@/shared/TypeBadge'
import { StatsStrip } from '@/status/StatsStrip'
import { StatusText } from '@/status/StatusText'
import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'

function AppDetailPage() {
  const { slug } = useParams<{ slug: string }>()
  const [logStream, setLogStream] = useState<LogStream>('all')

  const detailQuery = useAppDetail(slug ?? '')
  const logsQuery = useAppLogs(slug ?? '', { stream: logStream === 'all' ? undefined : logStream })

  const startMutation = useDetailStartApp()
  const stopMutation = useDetailStopApp()
  const restartMutation = useDetailRestartApp()
  const killMutation = useDetailKillApp()

  const app = detailQuery.data
  const logs = logsQuery.data

  const isTransitioning =
    startMutation.isPending ||
    stopMutation.isPending ||
    restartMutation.isPending ||
    killMutation.isPending ||
    (app ? ['starting', 'stopping', 'restarting'].includes(app.status) : false)

  if (!slug) {
    return <ErrorBanner message="No app slug provided" />
  }

  if (detailQuery.isLoading) {
    return (
      <div className="py-8">
        <Spinner />
      </div>
    )
  }

  if (detailQuery.error) {
    return (
      <ErrorBanner message={detailQuery.error instanceof Error ? detailQuery.error.message : 'Failed to load app'} />
    )
  }

  if (!app) {
    return <ErrorBanner message="App not found" />
  }

  const statItems = [
    { label: 'PID', value: app.pid ?? '--' },
    { label: 'Port', value: app.port ?? '--' },
    { label: 'Uptime', value: formatUptime(app.uptimeSeconds) },
    {
      label: 'Restarts',
      value: app.restartCount,
      color: app.restartCount > 0 ? ('amber' as const) : ('default' as const),
    },
    {
      label: 'Health',
      value: app.healthStatus ? formatHealthStatus(app.healthStatus) : '--',
      color:
        app.healthStatus === 'healthy'
          ? ('green' as const)
          : app.healthStatus === 'unhealthy'
            ? ('red' as const)
            : ('default' as const),
    },
    { label: 'Memory', value: formatMemory(app.resources?.memoryMb) },
  ]

  const identityRows = [
    { key: 'slug', label: 'Slug', value: app.name },
    { key: 'displayName', label: 'Display Name', value: app.displayName },
    {
      key: 'type',
      label: 'Type',
      value: <TypeBadge label={app.appType.name} />,
    },
    { key: 'registered', label: 'Registered', value: formatDateTime(app.registeredAt) },
    {
      key: 'tags',
      label: 'Tags',
      value:
        app.tags.length > 0 ? (
          <span className="flex items-center gap-1.5">
            {app.tags.map((tag) => (
              <TypeBadge key={tag.label} label={tag.label} />
            ))}
          </span>
        ) : (
          '--'
        ),
    },
  ]

  const routeRows = app.route
    ? [
        {
          key: 'domain',
          label: 'Domain',
          value: (
            <a
              href={`${app.route.tls ? 'https' : 'http'}://${app.route.domain}`}
              target="_blank"
              rel="noopener noreferrer"
              style={{ color: 'var(--wm-amber)', fontWeight: 500, textDecoration: 'none' }}
              onMouseEnter={(e) => {
                e.currentTarget.style.textDecoration = 'underline'
              }}
              onMouseLeave={(e) => {
                e.currentTarget.style.textDecoration = 'none'
              }}
            >
              {app.route.domain}
            </a>
          ),
        },
        { key: 'target', label: 'Target', value: app.route.target },
        {
          key: 'tls',
          label: 'TLS',
          value: app.route.tls ? (
            <span style={{ color: 'var(--wm-green)' }}>Enabled</span>
          ) : (
            <span style={{ color: 'var(--wm-text-dim)' }}>Disabled</span>
          ),
        },
      ]
    : []

  const resourceRows = app.resources
    ? [
        {
          key: 'cpu',
          label: 'CPU',
          value: app.resources.cpuPercent != null ? `${app.resources.cpuPercent.toFixed(1)}%` : '--',
        },
        { key: 'memory', label: 'Memory', value: formatMemory(app.resources.memoryMb) },
        { key: 'handles', label: 'Handles', value: app.resources.handleCount ?? '--' },
      ]
    : []

  return (
    <div className="flex flex-col" style={{ minHeight: 'calc(100vh - var(--wm-topbar-height) - 48px)' }}>
      {/* Breadcrumbs */}
      <Breadcrumbs
        segments={[{ label: 'Apps', to: ROUTES.apps }, { label: app.displayName }]}
        actions={
          <Link to={ROUTES.appSettings(slug)} style={{ textDecoration: 'none' }}>
            <ActionButton variant="ghost" size="sm">
              Settings
            </ActionButton>
          </Link>
        }
      />

      {/* Identity + actions row */}
      <div className="flex items-center justify-between mb-4">
        <div className="flex items-center gap-3">
          <h1 className="text-sm" style={{ color: 'var(--wm-text-bright)', fontWeight: 700 }}>
            {app.displayName}
          </h1>
          <TypeBadge label={app.appType.name} />
          <StatusText status={app.status} />
          {app.domain && (
            <>
              <span style={{ color: 'var(--wm-text-dim)', opacity: 0.3 }}>·</span>
              <a
                href={`${app.route?.tls ? 'https' : 'http'}://${app.domain}`}
                target="_blank"
                rel="noopener noreferrer"
                className="text-xs"
                style={{ color: 'var(--wm-text-dim)', textDecoration: 'none' }}
                onMouseEnter={(e) => {
                  e.currentTarget.style.color = 'var(--wm-amber)'
                }}
                onMouseLeave={(e) => {
                  e.currentTarget.style.color = 'var(--wm-text-dim)'
                }}
              >
                {app.domain}
              </a>
            </>
          )}
        </div>
        <ActionBar
          actions={app.actions}
          isTransitioning={isTransitioning}
          onStart={() => startMutation.mutate(slug)}
          onStop={() => stopMutation.mutate(slug)}
          onRestart={() => restartMutation.mutate(slug)}
          onKill={() => killMutation.mutate(slug)}
          onUpdate={() => {
            /* TODO: SSE update flow */
          }}
        />
      </div>

      {/* Stats strip */}
      <StatsStrip items={statItems} className="mt-4 mb-4" />

      {/* Detail cards grid */}
      <div className="grid grid-cols-2 gap-3 mb-4">
        <DetailCard title="Identity" rows={identityRows} />
        {routeRows.length > 0 && <DetailCard title="Route" rows={routeRows} />}
        {resourceRows.length > 0 && <DetailCard title="Resources" rows={resourceRows} />}
        {routeRows.length === 0 && resourceRows.length === 0 && (
          <div className="wm-detail-card flex items-center justify-center">
            <span className="text-xs" style={{ color: 'var(--wm-text-dim)' }}>
              No route configured
            </span>
          </div>
        )}
      </div>

      {/* Logs */}
      <SectionDivider label="Logs" className="mb-2" />
      <LogViewer
        entries={logs?.entries ?? []}
        totalBuffered={logs?.totalBuffered ?? 0}
        stream={logStream}
        onStreamChange={setLogStream}
        className="flex-1"
      />
    </div>
  )
}

export { AppDetailPage }
