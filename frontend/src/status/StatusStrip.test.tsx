import type { ProxyState } from '@/api/types'
import { render, screen } from '@testing-library/react'
import { describe, expect, test } from 'vitest'
import { StatusStrip } from './StatusStrip'
import { buildProxyStateCell } from './proxyStateCell'

function renderProxy(state: ProxyState) {
  return render(<StatusStrip cells={[buildProxyStateCell(state)]} />)
}

describe('StatusStrip + proxy cell', () => {
  test('renders Proxy label and value for running', () => {
    renderProxy('running')
    expect(screen.getByText('Proxy')).toBeInTheDocument()
    expect(screen.getByText('Running')).toBeInTheDocument()
  })

  test('applies green value class for running', () => {
    renderProxy('running')
    const value = screen.getByText('Running')
    expect(value).toHaveClass('wm-status-cell__value--green')
  })

  test('applies red value class for failed and shows remediation detail', () => {
    renderProxy('failed')
    const value = screen.getByText('Failed')
    expect(value).toHaveClass('wm-status-cell__value--red')
    expect(screen.getByText('Check logs, restart Collabhost')).toBeInTheDocument()
  })

  test('applies amber value class for disabled and shows actionable detail', () => {
    renderProxy('disabled')
    const value = screen.getByText('Disabled')
    expect(value).toHaveClass('wm-status-cell__value--amber')
    expect(screen.getByText('Re-run the installer or set COLLABHOST_PROXY_BINARY_PATH')).toBeInTheDocument()
  })

  test('applies amber value class for starting (transient warm-up)', () => {
    renderProxy('starting')
    const value = screen.getByText('Starting')
    expect(value).toHaveClass('wm-status-cell__value--amber')
  })

  test('applies no color class for stopped (neutral)', () => {
    renderProxy('stopped')
    const value = screen.getByText('Stopped')
    expect(value).not.toHaveClass('wm-status-cell__value--red')
    expect(value).not.toHaveClass('wm-status-cell__value--green')
    expect(value).not.toHaveClass('wm-status-cell__value--amber')
  })

  test('renders multiple cells alongside proxy cell', () => {
    render(
      <StatusStrip
        cells={[
          buildProxyStateCell('running'),
          { label: 'Total Apps', value: 5, color: 'amber' },
          { label: 'Issues', value: 0 },
        ]}
      />,
    )

    expect(screen.getByText('Proxy')).toBeInTheDocument()
    expect(screen.getByText('Total Apps')).toBeInTheDocument()
    expect(screen.getByText('Issues')).toBeInTheDocument()
  })
})
