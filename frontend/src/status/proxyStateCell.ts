import type { ProxyState } from '@/api/types'
import { formatProxyState, proxyStateDetail } from '@/lib/format'
import type { StatusCell } from './StatusStrip'

type ProxyStateColor = NonNullable<StatusCell['color']>

const COLOR_BY_STATE: Record<ProxyState, ProxyStateColor> = {
  starting: 'amber',
  running: 'green',
  failed: 'red',
  disabled: 'amber',
  stopped: 'default',
}

function proxyStateColor(state: ProxyState): ProxyStateColor {
  return COLOR_BY_STATE[state] ?? 'default'
}

function buildProxyStateCell(state: ProxyState): StatusCell {
  return {
    label: 'Proxy',
    value: formatProxyState(state),
    detail: proxyStateDetail(state),
    color: proxyStateColor(state),
  }
}

export { buildProxyStateCell, proxyStateColor }
