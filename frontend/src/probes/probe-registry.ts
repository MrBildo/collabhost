import type { ComponentType } from 'react'
import { DotnetDependenciesPanel } from './DotnetDependenciesPanel'
import { DotnetRuntimePanel } from './DotnetRuntimePanel'
import { NodePanel } from './NodePanel'
import { ReactPanel } from './ReactPanel'
import { TypeScriptPanel } from './TypeScriptPanel'

// biome-ignore lint/suspicious/noExplicitAny: dispatch table — each panel narrows its own typed props internally
const PROBE_PANELS: Record<string, ComponentType<{ data: any }>> = {
  'dotnet-runtime': DotnetRuntimePanel,
  'dotnet-dependencies': DotnetDependenciesPanel,
  node: NodePanel,
  react: ReactPanel,
  typescript: TypeScriptPanel,
}

export { PROBE_PANELS }
