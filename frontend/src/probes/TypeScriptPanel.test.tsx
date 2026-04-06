import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { TypeScriptPanel } from './TypeScriptPanel'

function makeData(overrides = {}) {
  return {
    version: '5.8.3',
    strict: true,
    target: 'ES2022',
    module: 'ESNext',
    ...overrides,
  }
}

describe('TypeScriptPanel', () => {
  test('renders version when present', () => {
    render(<TypeScriptPanel data={makeData()} />)
    expect(screen.getByText('Version')).toBeInTheDocument()
    expect(screen.getByText('5.8.3')).toBeInTheDocument()
  })

  test('hides version when null', () => {
    render(<TypeScriptPanel data={makeData({ version: null })} />)
    expect(screen.queryByText('Version')).not.toBeInTheDocument()
  })

  test('renders strict mode as Enabled when true', () => {
    render(<TypeScriptPanel data={makeData({ strict: true })} />)
    expect(screen.getByText('Strict Mode')).toBeInTheDocument()
    expect(screen.getByText('Enabled')).toBeInTheDocument()
  })

  test('renders strict mode as Disabled when false', () => {
    render(<TypeScriptPanel data={makeData({ strict: false })} />)
    expect(screen.getByText('Disabled')).toBeInTheDocument()
  })

  test('renders target when present', () => {
    render(<TypeScriptPanel data={makeData()} />)
    expect(screen.getByText('Target')).toBeInTheDocument()
    expect(screen.getByText('ES2022')).toBeInTheDocument()
  })

  test('hides target when null', () => {
    render(<TypeScriptPanel data={makeData({ target: null })} />)
    expect(screen.queryByText('Target')).not.toBeInTheDocument()
  })

  test('renders module when present', () => {
    render(<TypeScriptPanel data={makeData()} />)
    expect(screen.getByText('Module')).toBeInTheDocument()
    expect(screen.getByText('ESNext')).toBeInTheDocument()
  })

  test('hides module when null', () => {
    render(<TypeScriptPanel data={makeData({ module: null })} />)
    expect(screen.queryByText('Module')).not.toBeInTheDocument()
  })
})
