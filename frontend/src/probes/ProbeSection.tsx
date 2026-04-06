import type { ProbeEntry } from '@/api/types'
import { ProbePanel } from './ProbePanel'
import { UnknownProbePanel } from './UnknownProbePanel'
import { PROBE_PANELS } from './probe-registry'

type ProbeSectionProps = {
  probes: ProbeEntry[]
}

function ProbeSection({ probes }: ProbeSectionProps) {
  if (probes.length === 0) {
    return (
      <div className="py-3" style={{ color: 'var(--wm-text-dim)', fontSize: 'var(--wm-font-xs)' }}>
        No technology information detected
      </div>
    )
  }

  return (
    <div className="grid grid-cols-2 gap-3 mt-3">
      {probes.map((probe, index) => {
        const Panel = PROBE_PANELS[probe.type] ?? UnknownProbePanel
        return (
          <ProbePanel key={`${probe.type}-${index}`} title={probe.label}>
            <Panel data={probe.data} />
          </ProbePanel>
        )
      })}
    </div>
  )
}

export { ProbeSection }
export type { ProbeSectionProps }
