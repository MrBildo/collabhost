import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { UnknownProbePanel } from './UnknownProbePanel'

describe('UnknownProbePanel', () => {
  test('renders string values', () => {
    render(<UnknownProbePanel data={{ version: '3.12.0' }} />)
    expect(screen.getByText('Version')).toBeInTheDocument()
    expect(screen.getByText('3.12.0')).toBeInTheDocument()
  })

  test('renders number values', () => {
    render(<UnknownProbePanel data={{ count: 42 }} />)
    expect(screen.getByText('Count')).toBeInTheDocument()
    expect(screen.getByText('42')).toBeInTheDocument()
  })

  test('renders boolean true as Yes badge', () => {
    render(<UnknownProbePanel data={{ enabled: true }} />)
    expect(screen.getByText('Enabled')).toBeInTheDocument()
    expect(screen.getByText('Yes')).toBeInTheDocument()
  })

  test('renders boolean false as No badge', () => {
    render(<UnknownProbePanel data={{ enabled: false }} />)
    expect(screen.getByText('No')).toBeInTheDocument()
  })

  test('renders null values as --', () => {
    render(<UnknownProbePanel data={{ version: null }} />)
    expect(screen.getByText('--')).toBeInTheDocument()
  })

  test('renders empty data with No data message', () => {
    render(<UnknownProbePanel data={{}} />)
    expect(screen.getByText('No data')).toBeInTheDocument()
  })

  test('converts camelCase keys to title case', () => {
    render(<UnknownProbePanel data={{ moduleSystem: 'esm' }} />)
    expect(screen.getByText('Module System')).toBeInTheDocument()
  })

  test('JSON-stringifies complex values', () => {
    render(<UnknownProbePanel data={{ items: ['a', 'b'] }} />)
    expect(screen.getByText('["a","b"]')).toBeInTheDocument()
  })
})
