import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { DotnetDependenciesPanel } from './DotnetDependenciesPanel'

function makeData(overrides = {}) {
  return {
    packageCount: 47,
    projectReferenceCount: 1,
    notable: [
      { name: 'EF Core', version: '10.0.5' },
      { name: 'SQLite', version: null },
    ],
    ...overrides,
  }
}

describe('DotnetDependenciesPanel', () => {
  test('renders package count', () => {
    render(<DotnetDependenciesPanel data={makeData()} />)
    expect(screen.getByText('Packages')).toBeInTheDocument()
    expect(screen.getByText('47')).toBeInTheDocument()
  })

  test('renders project reference count when non-zero', () => {
    render(<DotnetDependenciesPanel data={makeData({ projectReferenceCount: 2 })} />)
    expect(screen.getByText('Project References')).toBeInTheDocument()
    expect(screen.getByText('2')).toBeInTheDocument()
  })

  test('hides project references when zero', () => {
    render(<DotnetDependenciesPanel data={makeData({ projectReferenceCount: 0 })} />)
    expect(screen.queryByText('Project References')).not.toBeInTheDocument()
  })

  test('renders notable packages with versions', () => {
    render(<DotnetDependenciesPanel data={makeData()} />)
    expect(screen.getByText('EF Core')).toBeInTheDocument()
    expect(screen.getByText('10.0.5')).toBeInTheDocument()
  })

  test('renders notable packages without version gracefully', () => {
    render(<DotnetDependenciesPanel data={makeData()} />)
    expect(screen.getByText('SQLite')).toBeInTheDocument()
  })

  test('hides notable section when no notable packages', () => {
    render(<DotnetDependenciesPanel data={makeData({ notable: [] })} />)
    expect(screen.queryByText('Notable')).not.toBeInTheDocument()
  })
})
