import { ActionButton } from '@/actions/ActionButton'
import type { AppListItem } from '@/api/types'
import { formatUptime } from '@/lib/format'
import { TypeBadge } from '@/shared/TypeBadge'
import { StatusDot } from '@/status/StatusDot'
import { StatusText } from '@/status/StatusText'
import type { Column } from './DataTable'

type AppColumnOptions = {
  onStart: (slug: string) => void
  onStop: (slug: string) => void
  isActionPending: boolean
}

/**
 * Status dot column -- narrow indicator column.
 */
function statusDotColumn(): Column<AppListItem> {
  return {
    key: 'status-dot',
    header: '',
    width: '28px',
    render: (app) => <StatusDot status={app.status} />,
  }
}

/**
 * Name column -- display name (bold) with slug below.
 * Sortable by display name.
 */
function nameColumn(): Column<AppListItem> {
  return {
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
  }
}

/**
 * Type badge column.
 */
function typeColumn(): Column<AppListItem> {
  return {
    key: 'type',
    header: 'Type',
    render: (app) => <TypeBadge label={app.appType.name} />,
  }
}

/**
 * Status text column.
 */
function statusColumn(): Column<AppListItem> {
  return {
    key: 'status',
    header: 'Status',
    render: (app) => <StatusText status={app.status} />,
  }
}

/**
 * Port column -- tabular-nums for alignment.
 */
function portColumn(): Column<AppListItem> {
  return {
    key: 'port',
    header: 'Port',
    render: (app) => (
      <span className="text-xs" style={{ color: 'var(--wm-text-dim)', fontVariantNumeric: 'tabular-nums' }}>
        {app.port ?? '--'}
      </span>
    ),
  }
}

/**
 * Uptime column -- tabular-nums for alignment.
 */
function uptimeColumn(): Column<AppListItem> {
  return {
    key: 'uptime',
    header: 'Uptime',
    render: (app) => (
      <span className="text-xs" style={{ color: 'var(--wm-text-dim)', fontVariantNumeric: 'tabular-nums' }}>
        {formatUptime(app.uptimeSeconds)}
      </span>
    ),
  }
}

/**
 * Domain column -- dimmed when inactive.
 */
function domainColumn(): Column<AppListItem> {
  return {
    key: 'domain',
    header: 'Domain',
    render: (app) =>
      app.domain ? (
        <a
          href={`${app.domainActive ? 'https' : 'http'}://${app.domain}`}
          target="_blank"
          rel="noopener noreferrer"
          className="text-xs"
          style={{ color: 'var(--wm-text-dim)', opacity: app.domainActive ? 1 : 0.5, textDecoration: 'none' }}
          onMouseEnter={(e) => {
            e.currentTarget.style.color = 'var(--wm-amber)'
          }}
          onMouseLeave={(e) => {
            e.currentTarget.style.color = 'var(--wm-text-dim)'
          }}
          onClick={(e) => e.stopPropagation()}
        >
          {app.domain}
        </a>
      ) : (
        <span className="text-xs" style={{ color: 'var(--wm-text-dim)', opacity: 0.5 }}>
          --
        </span>
      ),
  }
}

/**
 * Actions column -- start/stop buttons with stopPropagation to avoid row click.
 */
function actionsColumn({ onStart, onStop, isActionPending }: AppColumnOptions): Column<AppListItem> {
  return {
    key: 'actions',
    header: 'Actions',
    align: 'right',
    render: (app) => (
      // biome-ignore lint/a11y/useKeyWithClickEvents: stopPropagation prevents row navigation, buttons have own handlers
      <div className="flex items-center gap-1 justify-end" onClick={(e) => e.stopPropagation()}>
        {app.actions.canStart && (
          <ActionButton variant="success" size="sm" disabled={isActionPending} onClick={() => onStart(app.name)}>
            Start
          </ActionButton>
        )}
        {app.actions.canStop && (
          <ActionButton variant="default" size="sm" disabled={isActionPending} onClick={() => onStop(app.name)}>
            Stop
          </ActionButton>
        )}
      </div>
    ),
  }
}

/**
 * Build column set for the dashboard app table.
 * Compact: status dot, name, type, status, domain, actions.
 */
function buildDashboardColumns(options: AppColumnOptions): Column<AppListItem>[] {
  return [statusDotColumn(), nameColumn(), typeColumn(), statusColumn(), domainColumn(), actionsColumn(options)]
}

/**
 * Build column set for the full app list table.
 * Full: status dot, name, type, status, port, uptime, domain, actions.
 */
function buildAppListColumns(options: AppColumnOptions): Column<AppListItem>[] {
  return [
    statusDotColumn(),
    nameColumn(),
    typeColumn(),
    statusColumn(),
    portColumn(),
    uptimeColumn(),
    domainColumn(),
    actionsColumn(options),
  ]
}

export { buildDashboardColumns, buildAppListColumns }
