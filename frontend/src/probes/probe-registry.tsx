import type { KnownProbeEntry, ProbeEntry } from '@/api/types'
import type { ReactNode } from 'react'
import { DotnetDependenciesPanel } from './DotnetDependenciesPanel'
import { DotnetRuntimePanel } from './DotnetRuntimePanel'
import { ExecutablePanel } from './ExecutablePanel'
import { NodePanel } from './NodePanel'
import { ReactPanel } from './ReactPanel'
import { StaticSitePanel } from './StaticSitePanel'
import { TypeScriptPanel } from './TypeScriptPanel'
import { UnknownProbePanel } from './UnknownProbePanel'

// Typed probe dispatch (FE-TYPE-01). The previous shape was a
// `Record<string, ComponentType<{ data: any }>>` lookup — the `any` was forced
// by the old `ProbeEntry` catch-all collapsing the union's discriminant.
//
// A union that mixes literal-typed members with one open `type: string` member
// can't narrow on `probe.type` directly (every member is assignable to string),
// so we split the dispatch: `isKnownProbe` narrows to the discriminated
// `KnownProbeEntry`, after which the switch narrows each case to its own typed
// `data` with no `any`. A type the FE hasn't learned yet falls through to the
// UnknownProbePanel.
const KNOWN_PROBE_TYPES: ReadonlySet<KnownProbeEntry['type']> = new Set([
  'dotnet-runtime',
  'dotnet-dependencies',
  'node',
  'react',
  'typescript',
  'static-site',
  'executable',
])

function isKnownProbe(probe: ProbeEntry): probe is KnownProbeEntry {
  return KNOWN_PROBE_TYPES.has(probe.type as KnownProbeEntry['type'])
}

function renderKnownProbePanel(probe: KnownProbeEntry): ReactNode {
  switch (probe.type) {
    case 'dotnet-runtime':
      return <DotnetRuntimePanel data={probe.data} />
    case 'dotnet-dependencies':
      return <DotnetDependenciesPanel data={probe.data} />
    case 'node':
      return <NodePanel data={probe.data} />
    case 'react':
      return <ReactPanel data={probe.data} />
    case 'typescript':
      return <TypeScriptPanel data={probe.data} />
    case 'static-site':
      return <StaticSitePanel data={probe.data} />
    case 'executable':
      return <ExecutablePanel data={probe.data} />
  }
}

function renderProbePanel(probe: ProbeEntry): ReactNode {
  if (isKnownProbe(probe)) {
    return renderKnownProbePanel(probe)
  }
  // UnknownProbeEntry: `data` is `unknown`. Pass it through only when it is a
  // plain object the panel can enumerate; otherwise render an empty record so the
  // panel shows its "No data" state rather than crashing on `Object.entries`.
  const data = probe.data !== null && typeof probe.data === 'object' ? (probe.data as Record<string, unknown>) : {}
  return <UnknownProbePanel data={data} />
}

export { renderProbePanel }
