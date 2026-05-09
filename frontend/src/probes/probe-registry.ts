import type { ComponentType } from 'react'
import { DotnetDependenciesPanel } from './DotnetDependenciesPanel'
import { DotnetRuntimePanel } from './DotnetRuntimePanel'
import { ExecutablePanel } from './ExecutablePanel'
import { NodePanel } from './NodePanel'
import { ReactPanel } from './ReactPanel'
import { StaticSitePanel } from './StaticSitePanel'
import { TypeScriptPanel } from './TypeScriptPanel'

// biome-ignore lint/suspicious/noExplicitAny: dispatch table — each panel narrows its own typed props internally
const PROBE_PANELS: Record<string, ComponentType<{ data: any }>> = {
  'dotnet-runtime': DotnetRuntimePanel,
  'dotnet-dependencies': DotnetDependenciesPanel,
  node: NodePanel,
  react: ReactPanel,
  typescript: TypeScriptPanel,
  'static-site': StaticSitePanel,
  executable: ExecutablePanel,
}

export { PROBE_PANELS }
