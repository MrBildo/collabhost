import { describe, expect, test } from 'vitest'
import { DotnetDependenciesPanel } from './DotnetDependenciesPanel'
import { DotnetRuntimePanel } from './DotnetRuntimePanel'
import { NodePanel } from './NodePanel'
import { ReactPanel } from './ReactPanel'
import { TypeScriptPanel } from './TypeScriptPanel'
import { PROBE_PANELS } from './probe-registry'

describe('PROBE_PANELS', () => {
  test('maps dotnet-runtime to DotnetRuntimePanel', () => {
    expect(PROBE_PANELS['dotnet-runtime']).toBe(DotnetRuntimePanel)
  })

  test('maps dotnet-dependencies to DotnetDependenciesPanel', () => {
    expect(PROBE_PANELS['dotnet-dependencies']).toBe(DotnetDependenciesPanel)
  })

  test('maps node to NodePanel', () => {
    expect(PROBE_PANELS.node).toBe(NodePanel)
  })

  test('maps react to ReactPanel', () => {
    expect(PROBE_PANELS.react).toBe(ReactPanel)
  })

  test('maps typescript to TypeScriptPanel', () => {
    expect(PROBE_PANELS.typescript).toBe(TypeScriptPanel)
  })

  test('returns undefined for unknown type', () => {
    expect(PROBE_PANELS.python).toBeUndefined()
  })

  test('contains exactly five known panel types', () => {
    expect(Object.keys(PROBE_PANELS)).toHaveLength(5)
  })
})
