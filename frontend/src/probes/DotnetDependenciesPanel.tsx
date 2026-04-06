import type { DotnetDependenciesProbe } from '@/api/types'
import { ProbeRow, VersionValue } from './DotnetRuntimePanel'

type DotnetDependenciesPanelProps = {
  data: DotnetDependenciesProbe
}

function DotnetDependenciesPanel({ data }: DotnetDependenciesPanelProps) {
  return (
    <div className="flex flex-col gap-0">
      <ProbeRow label="Packages">
        <span style={{ color: 'var(--wm-text-bright)' }}>{data.packageCount}</span>
      </ProbeRow>
      {data.projectReferenceCount > 0 && (
        <ProbeRow label="Project References">
          <span style={{ color: 'var(--wm-text-bright)' }}>{data.projectReferenceCount}</span>
        </ProbeRow>
      )}
      {data.notable.length > 0 && (
        <ProbeRow label="Notable">
          <span className="flex flex-wrap gap-1 justify-end">
            {data.notable.map((dep) => (
              <span key={dep.name} className="wm-probe-dep">
                <span style={{ color: 'var(--wm-text-bright)' }}>{dep.name}</span>
                {dep.version && <VersionValue version={dep.version} />}
              </span>
            ))}
          </span>
        </ProbeRow>
      )}
    </div>
  )
}

export { DotnetDependenciesPanel }
export type { DotnetDependenciesPanelProps }
