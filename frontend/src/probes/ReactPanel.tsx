import type { ReactProbe } from '@/api/types'
import { ProbeRow, VersionValue } from './DotnetRuntimePanel'

type ReactPanelProps = {
  data: ReactProbe
}

function formatIdentifier(value: string | null): React.ReactNode {
  if (!value) return <span style={{ color: 'var(--wm-text-dim)' }}>Not detected</span>
  // Convert kebab-case identifiers to title case: "react-router" -> "React Router"
  const display = value
    .split('-')
    .map((word) => word.charAt(0).toUpperCase() + word.slice(1))
    .join(' ')
  return <span style={{ color: 'var(--wm-text-bright)' }}>{display}</span>
}

function ReactPanel({ data }: ReactPanelProps) {
  return (
    <div className="flex flex-col gap-0">
      <ProbeRow label="Version">
        <VersionValue version={data.version} />
      </ProbeRow>
      {data.bundler && (
        <ProbeRow label="Bundler">
          <span>
            {formatIdentifier(data.bundler)}
            {data.bundlerVersion && (
              <>
                {' '}
                <VersionValue version={data.bundlerVersion} />
              </>
            )}
          </span>
        </ProbeRow>
      )}
      {data.router && <ProbeRow label="Router">{formatIdentifier(data.router)}</ProbeRow>}
      {data.stateManagement && <ProbeRow label="State">{formatIdentifier(data.stateManagement)}</ProbeRow>}
      {data.cssStrategy && <ProbeRow label="CSS">{formatIdentifier(data.cssStrategy)}</ProbeRow>}
    </div>
  )
}

export { ReactPanel }
export type { ReactPanelProps }
