import type { ProbeEntry } from '@/api/types'
import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { renderProbePanel } from './probe-registry'

describe('renderProbePanel', () => {
  test('renders the dotnet-runtime panel for a dotnet-runtime probe', () => {
    const probe: ProbeEntry = {
      type: 'dotnet-runtime',
      label: '.NET Runtime',
      data: { tfm: 'net10.0', runtimeVersion: '10.0.0', isAspNetCore: true, isSelfContained: false, serverGc: true },
    }
    render(<div>{renderProbePanel(probe)}</div>)
    expect(screen.getByText('Target Framework')).toBeInTheDocument()
  })

  test('renders the node panel for a node probe', () => {
    const probe: ProbeEntry = {
      type: 'node',
      label: 'Node',
      data: {
        engineVersion: '22.0.0',
        packageManager: 'npm',
        packageManagerVersion: '10.0.0',
        moduleSystem: 'esm',
        dependencyCount: 3,
        devDependencyCount: 2,
      },
    }
    render(<div>{renderProbePanel(probe)}</div>)
    expect(screen.getByText('Module System')).toBeInTheDocument()
  })

  test('renders the typescript panel for a typescript probe', () => {
    const probe: ProbeEntry = {
      type: 'typescript',
      label: 'TypeScript',
      data: { version: '5.6.0', strict: true, target: 'ES2022', module: 'ESNext' },
    }
    render(<div>{renderProbePanel(probe)}</div>)
    expect(screen.getByText('Strict Mode')).toBeInTheDocument()
  })

  test('renders the UnknownProbePanel for a forward-compat unknown probe type', () => {
    const probe: ProbeEntry = {
      type: 'python',
      label: 'Python',
      data: { pythonVersion: '3.12' },
    }
    render(<div>{renderProbePanel(probe)}</div>)
    // UnknownProbePanel camelToTitle's the keys; "pythonVersion" -> "Python Version".
    expect(screen.getByText('Python Version')).toBeInTheDocument()
  })

  test('UnknownProbePanel shows the empty state when an unknown probe has non-object data', () => {
    const probe: ProbeEntry = {
      type: 'weird',
      label: 'Weird',
      data: 'not-an-object',
    }
    render(<div>{renderProbePanel(probe)}</div>)
    expect(screen.getByText('No data')).toBeInTheDocument()
  })
})
