import type { ExecutableProbe } from '@/api/types'
import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { ExecutablePanel } from './ExecutablePanel'

function makeData(overrides: Partial<ExecutableProbe> = {}): ExecutableProbe {
  return {
    binaryName: 'myapp.exe',
    binarySizeBytes: 1024 * 1024 * 8, // 8 MB
    candidateBinaryCount: 1,
    isManagedDotnet: false,
    ...overrides,
  }
}

describe('ExecutablePanel', () => {
  test('renders binary name', () => {
    render(<ExecutablePanel data={makeData({ binaryName: 'tool.exe' })} />)
    expect(screen.getByText('Binary')).toBeInTheDocument()
    expect(screen.getByText('tool.exe')).toBeInTheDocument()
  })

  test('renders binary size in human format', () => {
    render(<ExecutablePanel data={makeData({ binarySizeBytes: 1024 * 1024 * 16 })} />)
    expect(screen.getByText('Size')).toBeInTheDocument()
    expect(screen.getByText('16.0 MB')).toBeInTheDocument()
  })

  test('does not show candidate summary for single binary', () => {
    render(<ExecutablePanel data={makeData({ candidateBinaryCount: 1 })} />)
    expect(screen.queryByText(/of \d+ candidates/)).not.toBeInTheDocument()
  })

  test('shows candidate summary when multiple binaries detected', () => {
    render(<ExecutablePanel data={makeData({ binaryName: 'first.exe', candidateBinaryCount: 3 })} />)
    expect(screen.getByText('first.exe')).toBeInTheDocument()
    expect(screen.getByText('1 of 3 candidates')).toBeInTheDocument()
  })

  test('does not render the dotnet nudge when isManagedDotnet is false', () => {
    render(<ExecutablePanel data={makeData({ isManagedDotnet: false })} />)
    expect(screen.queryByText(/Looks like \.NET/)).not.toBeInTheDocument()
    expect(screen.queryByText(/dotnet-app/)).not.toBeInTheDocument()
  })

  test('renders the dotnet nudge when isManagedDotnet is true', () => {
    render(<ExecutablePanel data={makeData({ isManagedDotnet: true })} />)
    expect(screen.getByText(/Looks like \.NET/)).toBeInTheDocument()
    expect(screen.getByText('dotnet-app')).toBeInTheDocument()
    expect(screen.getByText(/health checks, environment variables, port injection/)).toBeInTheDocument()
  })

  test('handles small binary size', () => {
    render(<ExecutablePanel data={makeData({ binarySizeBytes: 512 })} />)
    expect(screen.getByText('512 B')).toBeInTheDocument()
  })

  test('handles binary name without .exe suffix', () => {
    render(<ExecutablePanel data={makeData({ binaryName: 'app' })} />)
    expect(screen.getByText('app')).toBeInTheDocument()
  })
})
