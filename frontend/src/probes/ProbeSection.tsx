import type { ProbeEntry } from '@/api/types'
import { ProbePanel } from './ProbePanel'
import { renderProbePanel } from './probe-registry'

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
      {probes.map((probe, index) => (
        <ProbePanel key={`${probe.type}-${index}`} title={probe.label}>
          {renderProbePanel(probe)}
        </ProbePanel>
      ))}
    </div>
  )
}

export { ProbeSection }
export type { ProbeSectionProps }
