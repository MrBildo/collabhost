import { Breadcrumbs } from '@/chrome/Breadcrumbs'
import { useSystemStatus } from '@/hooks/use-system-status'
import { formatUptimeLong } from '@/lib/format'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { Spinner } from '@/shared/Spinner'
import { StatusStrip } from '@/status/StatusStrip'

function SystemPage() {
  const statusQuery = useSystemStatus()
  const status = statusQuery.data

  const statusCells = status
    ? [
        {
          label: 'Status',
          value: status.status,
          color: status.status === 'ok' ? ('green' as const) : ('red' as const),
        },
        { label: 'Hostname', value: status.hostname },
        { label: 'Version', value: status.version, color: 'amber' as const },
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
      <Breadcrumbs segments={[{ label: 'System' }]} />

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
                    {status.version}
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
                    {status.status}
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
