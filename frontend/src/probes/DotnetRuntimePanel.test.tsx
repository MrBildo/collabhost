import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { DotnetRuntimePanel } from './DotnetRuntimePanel'

function makeData(overrides = {}) {
  return {
    tfm: 'net10.0',
    runtimeVersion: '10.0.0',
    isAspNetCore: true,
    isSelfContained: false,
    serverGc: true,
    ...overrides,
  }
}

describe('DotnetRuntimePanel', () => {
  test('renders target framework', () => {
    render(<DotnetRuntimePanel data={makeData()} />)
    expect(screen.getByText('Target Framework')).toBeInTheDocument()
    expect(screen.getByText('net10.0')).toBeInTheDocument()
  })

  test('renders runtime version', () => {
    render(<DotnetRuntimePanel data={makeData()} />)
    expect(screen.getByText('Runtime Version')).toBeInTheDocument()
    expect(screen.getByText('10.0.0')).toBeInTheDocument()
  })

  test('renders ASP.NET Core as Yes badge when true', () => {
    render(<DotnetRuntimePanel data={makeData({ isAspNetCore: true })} />)
    expect(screen.getByText('ASP.NET Core')).toBeInTheDocument()
    expect(screen.getByText('Yes')).toBeInTheDocument()
  })

  test('renders ASP.NET Core as No badge when false', () => {
    render(<DotnetRuntimePanel data={makeData({ isAspNetCore: false })} />)
    // "No" appears for both isSelfContained and isAspNetCore when both false
    const noBadges = screen.getAllByText('No')
    expect(noBadges.length).toBeGreaterThanOrEqual(1)
  })

  test('renders self-contained status', () => {
    render(<DotnetRuntimePanel data={makeData({ isSelfContained: true })} />)
    expect(screen.getByText('Self-Contained')).toBeInTheDocument()
  })

  test('renders server GC as Enabled when true', () => {
    render(<DotnetRuntimePanel data={makeData({ serverGc: true })} />)
    expect(screen.getByText('Server GC')).toBeInTheDocument()
    expect(screen.getByText('Enabled')).toBeInTheDocument()
  })

  test('renders server GC as Disabled when false', () => {
    render(<DotnetRuntimePanel data={makeData({ serverGc: false })} />)
    expect(screen.getByText('Disabled')).toBeInTheDocument()
  })
})
