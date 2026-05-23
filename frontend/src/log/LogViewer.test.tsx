import type { StreamEntry } from '@/api/types'
import { render, screen } from '@testing-library/react'
import { beforeAll, describe, expect, test, vi } from 'vitest'
import { LogViewer } from './LogViewer'

beforeAll(() => {
  // jsdom doesn't implement scrollHeight on Element; LogViewer's useLayoutEffect
  // reads scrollRef.current.scrollHeight on mount. Stub to a stable number so
  // the effect runs without throwing.
  Object.defineProperty(HTMLDivElement.prototype, 'scrollHeight', {
    configurable: true,
    get: () => 100,
  })
})

function renderViewer(streamMode: 'live' | 'polling' | 'reconnecting', entries: StreamEntry[] = []) {
  return render(
    <LogViewer
      entries={entries}
      totalBuffered={entries.length}
      stream="all"
      onStreamChange={vi.fn()}
      streamMode={streamMode}
    />,
  )
}

describe('LogViewer stream-mode indicator (#321)', () => {
  test('renders no indicator when stream is live', () => {
    renderViewer('live')
    expect(screen.queryByTestId('stream-mode-indicator')).toBeNull()
  })

  test('renders polling indicator when SSE fell back to /logs polling', () => {
    renderViewer('polling')
    const indicator = screen.getByTestId('stream-mode-indicator')
    expect(indicator).toHaveAttribute('data-mode', 'polling')
    expect(indicator).toHaveTextContent('polling')
  })

  test('renders reconnecting indicator during the SSE-disconnect neither-mode window', () => {
    renderViewer('reconnecting')
    const indicator = screen.getByTestId('stream-mode-indicator')
    expect(indicator).toHaveAttribute('data-mode', 'reconnecting')
    expect(indicator).toHaveTextContent('reconnecting')
  })

  test('indicator carries a hover-title explaining the degraded state', () => {
    renderViewer('polling')
    const title = screen.getByTestId('stream-mode-indicator').getAttribute('title')
    expect(title).toContain('polling')
  })
})
