import { useSystemStatus } from '@/hooks/use-system-status'
import { formatUptimeLong } from '@/lib/format'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { Spinner } from '@/shared/Spinner'
import { StatusStrip } from '@/status/StatusStrip'

function formatStatusLabel(value: string): string {
  if (value === 'ok') return 'OK'
  return value.charAt(0).toUpperCase() + value.slice(1)
}

function displayVersion(version: string): string {
  const plusIndex = version.indexOf('+')
  if (plusIndex < 0) return version
  return version.slice(0, plusIndex)
}

function SystemPage() {
  const statusQuery = useSystemStatus()
  const status = statusQuery.data

  const statusCells = status
    ? [
        {
          label: 'Status',
          value: formatStatusLabel(status.status),
          color: status.status === 'ok' ? ('green' as const) : ('red' as const),
        },
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
                    Uptime
                  </td>
                  <td className="text-xs" style={{ color: 'var(--wm-text-bright)' }}>
                    {formatUptimeLong(status.uptimeSeconds)}
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
