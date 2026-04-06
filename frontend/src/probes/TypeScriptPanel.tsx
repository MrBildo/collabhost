import type { TypeScriptProbe } from '@/api/types'
import { BooleanBadge, ProbeRow, VersionValue } from './DotnetRuntimePanel'

type TypeScriptPanelProps = {
  data: TypeScriptProbe
}

function TypeScriptPanel({ data }: TypeScriptPanelProps) {
  return (
    <div className="flex flex-col gap-0">
      {data.version && (
        <ProbeRow label="Version">
          <VersionValue version={data.version} />
        </ProbeRow>
      )}
      <ProbeRow label="Strict Mode">
        <BooleanBadge value={data.strict} trueLabel="Enabled" falseLabel="Disabled" />
      </ProbeRow>
      {data.target && (
        <ProbeRow label="Target">
          <span style={{ color: 'var(--wm-text-bright)' }}>{data.target}</span>
        </ProbeRow>
      )}
      {data.module && (
        <ProbeRow label="Module">
          <span style={{ color: 'var(--wm-text-bright)' }}>{data.module}</span>
        </ProbeRow>
      )}
    </div>
  )
}

export { TypeScriptPanel }
export type { TypeScriptPanelProps }
