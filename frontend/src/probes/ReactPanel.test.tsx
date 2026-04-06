import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { ReactPanel } from './ReactPanel'

function makeData(overrides = {}) {
  return {
    version: '19.1.0',
    bundler: 'vite',
    bundlerVersion: '6.2.0',
    router: 'react-router',
    stateManagement: 'tanstack-query',
    cssStrategy: 'css-custom-properties',
    ...overrides,
  }
}

describe('ReactPanel', () => {
  test('renders React version', () => {
    render(<ReactPanel data={makeData()} />)
    expect(screen.getByText('Version')).toBeInTheDocument()
    expect(screen.getByText('19.1.0')).toBeInTheDocument()
  })

  test('renders bundler with version', () => {
    render(<ReactPanel data={makeData()} />)
    expect(screen.getByText('Bundler')).toBeInTheDocument()
    expect(screen.getByText('Vite')).toBeInTheDocument()
    expect(screen.getByText('6.2.0')).toBeInTheDocument()
  })

  test('hides bundler when null', () => {
    render(<ReactPanel data={makeData({ bundler: null, bundlerVersion: null })} />)
    expect(screen.queryByText('Bundler')).not.toBeInTheDocument()
  })

  test('renders router formatted as title case', () => {
    render(<ReactPanel data={makeData()} />)
    expect(screen.getByText('Router')).toBeInTheDocument()
    expect(screen.getByText('React Router')).toBeInTheDocument()
  })

  test('hides router when null', () => {
    render(<ReactPanel data={makeData({ router: null })} />)
    expect(screen.queryByText('Router')).not.toBeInTheDocument()
  })

  test('renders state management formatted', () => {
    render(<ReactPanel data={makeData()} />)
    expect(screen.getByText('State')).toBeInTheDocument()
    expect(screen.getByText('Tanstack Query')).toBeInTheDocument()
  })

  test('hides state management when null', () => {
    render(<ReactPanel data={makeData({ stateManagement: null })} />)
    expect(screen.queryByText('State')).not.toBeInTheDocument()
  })

  test('renders CSS strategy formatted', () => {
    render(<ReactPanel data={makeData()} />)
    expect(screen.getByText('CSS')).toBeInTheDocument()
    expect(screen.getByText('Css Custom Properties')).toBeInTheDocument()
  })

  test('hides CSS strategy when null', () => {
    render(<ReactPanel data={makeData({ cssStrategy: null })} />)
    expect(screen.queryByText('CSS')).not.toBeInTheDocument()
  })
})
