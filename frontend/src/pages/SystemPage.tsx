import { useSystemStatus } from '@/hooks/use-system-status'
import { formatProxyState, formatUptimeLong, proxyStateDetail } from '@/lib/format'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { Spinner } from '@/shared/Spinner'
import type { StatusCell } from '@/status/StatusStrip'
import { StatusStrip } from '@/status/StatusStrip'
import { buildProxyStateCell, proxyStateColor } from '@/status/proxyStateCell'

function formatStatusLabel(value: string): string {
  if (value === 'ok') return 'OK'
  return value.charAt(0).toUpperCase() + value.slice(1)
}

function displayVersion(version: string): string {
  const plusIndex = version.indexOf('+')
  if (plusIndex < 0) return version
  return version.slice(0, plusIndex)
}

function proxyColorVar(color: 'amber' | 'green' | 'red' | 'default'): string {
  switch (color) {
    case 'green':
      return 'var(--wm-green)'
    case 'red':
      return 'var(--wm-red)'
    case 'amber':
      return 'var(--wm-amber)'
    default:
      return 'var(--wm-text-dim)'
  }
}

function SystemPage() {
  const statusQuery = useSystemStatus()
  const status = statusQuery.data

  const statusCells: StatusCell[] = status
    ? [
        {
          label: 'Status',
          value: formatStatusLabel(status.status),
          color: status.status === 'ok' ? ('green' as const) : ('red' as const),
        },
        buildProxyStateCell(status.proxyState),
        { label: 'Hostname', value: status.hostname },
        { label: 'Version', value: displayVersion(status.version), color: 'amber' as const },
        { label: 'Uptime', value: formatUptimeLong(status.uptimeSeconds) },
      ]
    : []

  if (statusQuery.isLoading) {
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
          <span style={{ color: 'var(--wm-text-dim)' }}>{'// '}</span>System
        </h1>
      </div>

      {statusQuery.error && (
        <ErrorBanner
          message={statusQuery.error instanceof Error ? statusQuery.error.message : 'Failed to load system status'}
          className="mb-4"
        />
      )}

      {status && (
        <>
          <StatusStrip cells={statusCells} className="mb-5" />

          <div className="wm-panel p-4">
            <table className="wm-table">
              <tbody>
                <tr>
                  <td className="text-xs font-semibold" style={{ color: 'var(--wm-text-dim)', width: '140px' }}>
                    Hostname
                  </td>
                  <td className="text-xs" style={{ color: 'var(--wm-text-bright)' }}>
                    {status.hostname}
                  </td>
                </tr>
                <tr>
                  <td className="text-xs font-semibold" style={{ color: 'var(--wm-text-dim)' }}>
                    Version
                  </td>
                  <td className="text-xs" style={{ color: 'var(--wm-text-bright)' }}>
                    {displayVersion(status.version)}
                  </td>
                </tr>
                <tr>
                  <td className="text-xs font-semibold" style={{ color: 'var(--wm-text-dim)' }}>
                    Status
                  </td>
                  <td
                    className="text-xs"
                    style={{ color: status.status === 'ok' ? 'var(--wm-green)' : 'var(--wm-red)' }}
                  >
                    {formatStatusLabel(status.status)}
                  </td>
                </tr>
                <tr>
                  <td className="text-xs font-semibold" style={{ color: 'var(--wm-text-dim)' }}>
                    Proxy
                  </td>
                  <td className="text-xs" style={{ color: proxyColorVar(proxyStateColor(status.proxyState)) }}>
                    {formatProxyState(status.proxyState)}
                    {proxyStateDetail(status.proxyState) && (
                      <span style={{ color: 'var(--wm-text-dim)', marginLeft: 8 }}>
                        — {proxyStateDetail(status.proxyState)}
                      </span>
                    )}
                  </td>
                </tr>
                <tr>
                  <td className="text-xs font-semibold" style={{ color: 'var(--wm-text-dim)' }}>
                    Uptime
                  </td>
                  <td className="text-xs" style={{ color: 'var(--wm-text-bright)' }}>
                    {formatUptimeLong(status.uptimeSeconds)}
                  </td>
                </tr>
                <tr>
                  <td className="text-xs font-semibold" style={{ color: 'var(--wm-text-dim)' }}>
                    Portal URL
                  </td>
                  <td className="text-xs">
                    <a
                      href={status.portalUrl}
                      target="_blank"
                      rel="noopener noreferrer"
                      style={{ color: 'var(--wm-amber)', textDecoration: 'none' }}
                      onMouseEnter={(e) => {
                        e.currentTarget.style.color = 'var(--wm-text-bright)'
                      }}
                      onMouseLeave={(e) => {
                        e.currentTarget.style.color = 'var(--wm-amber)'
                      }}
                    >
                      {status.portalUrl}
                    </a>
                  </td>
                </tr>
                <tr>
                  <td className="text-xs font-semibold" style={{ color: 'var(--wm-text-dim)' }}>
                    Timestamp
                  </td>
                  <td
                    className="text-xs"
                    style={{ color: 'var(--wm-text-bright)', fontVariantNumeric: 'tabular-nums' }}
                  >
                    {new Date(status.timestamp).toLocaleString()}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  )
}

export { SystemPage }
