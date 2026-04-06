import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { NodePanel } from './NodePanel'

function makeData(overrides = {}) {
  return {
    engineVersion: '>=22.0.0',
    packageManager: 'pnpm',
    packageManagerVersion: '9.15.4',
    moduleSystem: 'esm' as const,
    dependencyCount: 14,
    devDependencyCount: 12,
    ...overrides,
  }
}

describe('NodePanel', () => {
  test('renders engine version when present', () => {
    render(<NodePanel data={makeData()} />)
    expect(screen.getByText('Engine')).toBeInTheDocument()
    expect(screen.getByText('>=22.0.0')).toBeInTheDocument()
  })

  test('hides engine row when null', () => {
    render(<NodePanel data={makeData({ engineVersion: null })} />)
    expect(screen.queryByText('Engine')).not.toBeInTheDocument()
  })

  test('renders package manager with version', () => {
    render(<NodePanel data={makeData()} />)
    expect(screen.getByText('Package Manager')).toBeInTheDocument()
    expect(screen.getByText('Pnpm')).toBeInTheDocument()
    expect(screen.getByText('9.15.4')).toBeInTheDocument()
  })

  test('renders Not detected when package manager is null', () => {
    render(<NodePanel data={makeData({ packageManager: null, packageManagerVersion: null })} />)
    expect(screen.getByText('Not detected')).toBeInTheDocument()
  })

  test('renders ESM for esm module system', () => {
    render(<NodePanel data={makeData({ moduleSystem: 'esm' })} />)
    expect(screen.getByText('ESM')).toBeInTheDocument()
  })

  test('renders CommonJS for commonjs module system', () => {
    render(<NodePanel data={makeData({ moduleSystem: 'commonjs' })} />)
    expect(screen.getByText('CommonJS')).toBeInTheDocument()
  })

  test('renders Not detected for null module system', () => {
    render(<NodePanel data={makeData({ moduleSystem: null })} />)
    expect(screen.getByText('Not detected')).toBeInTheDocument()
  })

  test('renders dependency counts', () => {
    render(<NodePanel data={makeData()} />)
    expect(screen.getByText('Dependencies')).toBeInTheDocument()
    // Counts and labels are in sibling text nodes within one span
    expect(screen.getByText('prod')).toBeInTheDocument()
    expect(screen.getByText('dev')).toBeInTheDocument()
    expect(screen.getByText(/14/)).toBeInTheDocument()
    expect(screen.getByText(/12/)).toBeInTheDocument()
  })
})
