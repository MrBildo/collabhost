import type { NodeProbe } from '@/api/types'
import { ProbeRow, VersionValue } from './DotnetRuntimePanel'

type NodePanelProps = {
  data: NodeProbe
}

function formatModuleSystem(value: 'esm' | 'commonjs' | null): string {
  if (value === 'esm') return 'ESM'
  if (value === 'commonjs') return 'CommonJS'
  return 'Not detected'
}

function formatPackageManager(name: string | null, version: string | null): React.ReactNode {
  if (!name) return <span style={{ color: 'var(--wm-text-dim)' }}>Not detected</span>
  const display = name.charAt(0).toUpperCase() + name.slice(1)
  if (version) {
    return (
      <span>
        <span style={{ color: 'var(--wm-text-bright)' }}>{display}</span> <VersionValue version={version} />
      </span>
    )
  }
  return <span style={{ color: 'var(--wm-text-bright)' }}>{display}</span>
}

function NodePanel({ data }: NodePanelProps) {
  return (
    <div className="flex flex-col gap-0">
      {data.engineVersion && (
        <ProbeRow label="Engine">
          <VersionValue version={data.engineVersion} />
        </ProbeRow>
      )}
      <ProbeRow label="Package Manager">
        {formatPackageManager(data.packageManager, data.packageManagerVersion)}
      </ProbeRow>
      <ProbeRow label="Module System">
        <span style={{ color: data.moduleSystem ? 'var(--wm-text-bright)' : 'var(--wm-text-dim)' }}>
          {formatModuleSystem(data.moduleSystem)}
        </span>
      </ProbeRow>
      <ProbeRow label="Dependencies">
        <span style={{ color: 'var(--wm-text-bright)' }}>
          {data.dependencyCount}
          <span style={{ color: 'var(--wm-text-dim)', marginLeft: 4 }}>prod</span>
          <span style={{ color: 'var(--wm-text-dim)', margin: '0 4px' }}>/</span>
          {data.devDependencyCount}
          <span style={{ color: 'var(--wm-text-dim)', marginLeft: 4 }}>dev</span>
        </span>
      </ProbeRow>
    </div>
  )
}

export { NodePanel }
export type { NodePanelProps }
