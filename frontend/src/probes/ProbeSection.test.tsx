import type { ProbeEntry } from '@/api/types'
import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { ProbeSection } from './ProbeSection'

function makeDotnetRuntimeProbe(): ProbeEntry {
  return {
    type: 'dotnet-runtime',
    label: '.NET Runtime',
    data: {
      tfm: 'net10.0',
      runtimeVersion: '10.0.0',
      isAspNetCore: true,
      isSelfContained: false,
      serverGc: true,
    },
  }
}

function makeNodeProbe(): ProbeEntry {
  return {
    type: 'node',
    label: 'Node.js',
    data: {
      engineVersion: '>=22.0.0',
      packageManager: 'pnpm',
      packageManagerVersion: '9.15.4',
      moduleSystem: 'esm' as const,
      dependencyCount: 14,
      devDependencyCount: 12,
    },
  }
}

function makeUnknownProbe(): ProbeEntry {
  return {
    type: 'python',
    label: 'Python',
    data: {
      version: '3.12.0',
      virtualEnv: true,
    },
  }
}

describe('ProbeSection', () => {
  test('renders empty state when probes array is empty', () => {
    render(<ProbeSection probes={[]} />)
    expect(screen.getByText('No technology information detected')).toBeInTheDocument()
  })

  test('renders panel with label from probe entry', () => {
    render(<ProbeSection probes={[makeDotnetRuntimeProbe()]} />)
    expect(screen.getByText('.NET Runtime')).toBeInTheDocument()
  })

  test('renders multiple panels for multiple probes', () => {
    render(<ProbeSection probes={[makeDotnetRuntimeProbe(), makeNodeProbe()]} />)
    expect(screen.getByText('.NET Runtime')).toBeInTheDocument()
    expect(screen.getByText('Node.js')).toBeInTheDocument()
  })

  test('renders fallback panel for unknown probe types', () => {
    render(<ProbeSection probes={[makeUnknownProbe()]} />)
    expect(screen.getByText('Python')).toBeInTheDocument()
    // UnknownProbePanel should render the data as key-value pairs
    expect(screen.getByText('3.12.0')).toBeInTheDocument()
  })

  test('renders known and unknown probes together', () => {
    render(<ProbeSection probes={[makeDotnetRuntimeProbe(), makeUnknownProbe()]} />)
    expect(screen.getByText('.NET Runtime')).toBeInTheDocument()
    expect(screen.getByText('Python')).toBeInTheDocument()
  })
})
