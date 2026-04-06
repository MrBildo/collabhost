import { ProbeRow } from './DotnetRuntimePanel'
import { camelToTitle } from './probe-format'

type UnknownProbePanelProps = {
  data: Record<string, unknown>
}

function formatValue(value: unknown): React.ReactNode {
  if (value === null || value === undefined) {
    return <span style={{ color: 'var(--wm-text-dim)' }}>--</span>
  }
  if (typeof value === 'boolean') {
    return (
      <span
        className="wm-probe-badge"
        style={{
          color: value ? 'var(--wm-green)' : 'var(--wm-text-dim)',
          borderColor: value ? 'var(--wm-green-border)' : 'var(--wm-border)',
          background: value ? 'var(--wm-green-dim)' : 'transparent',
        }}
      >
        {value ? 'Yes' : 'No'}
      </span>
    )
  }
  if (typeof value === 'number') {
    return <span style={{ color: 'var(--wm-text-bright)' }}>{value}</span>
  }
  if (typeof value === 'string') {
    return <span style={{ color: 'var(--wm-text-bright)' }}>{value}</span>
  }
  // Arrays and objects: JSON-stringify
  return (
    <span className="wm-probe-version" style={{ fontSize: 'var(--wm-font-2xs)' }}>
      {JSON.stringify(value)}
    </span>
  )
}

function UnknownProbePanel({ data }: UnknownProbePanelProps) {
  const entries = Object.entries(data)

  if (entries.length === 0) {
    return (
      <div className="py-2" style={{ color: 'var(--wm-text-dim)', fontSize: 'var(--wm-font-xs)' }}>
        No data
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-0">
      {entries.map(([key, value]) => (
        <ProbeRow key={key} label={camelToTitle(key)}>
          {formatValue(value)}
        </ProbeRow>
      ))}
    </div>
  )
}

export { UnknownProbePanel }
export type { UnknownProbePanelProps }
