import type { StreamEntry } from '@/api/types'
import { fireEvent, render, screen } from '@testing-library/react'
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
  // jsdom returns 0 for clientHeight; provide a stable height so the
  // isAtBottom math (scrollHeight - scrollTop - clientHeight) is meaningful.
  Object.defineProperty(HTMLDivElement.prototype, 'clientHeight', {
    configurable: true,
    get: () => 50,
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

function makeLogEntry(id: number, content: string): StreamEntry {
  return {
    type: 'log',
    entry: { id, timestamp: '2026-04-07T12:00:00Z', stream: 'stdout', content },
  }
}

// The scroll container is the element carrying onScroll/onWheel — the only div
// with the wm-log-viewer class.
function getScroller(container: HTMLElement): HTMLDivElement {
  const el = container.querySelector('.wm-log-viewer')
  if (!el) throw new Error('log scroll container not found')
  return el as HTMLDivElement
}

function isFollowing(): boolean {
  const follow = screen.getByRole('button', { name: 'Follow' })
  return follow.className.includes('wm-filter-chip--active')
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

describe('LogViewer follow-mode release on any user scroll (FE-UI-01)', () => {
  test('a scroll-up away from bottom releases follow even with no wheel event', () => {
    // Keyboard (PageUp), scrollbar drag, and touch all fire `scroll` without a
    // preceding `wheel`. Before the fix, follow only released on wheel, so the
    // auto-scroller fought every non-wheel input. This scroll has no wheel.
    const { container } = renderViewer('live', [makeLogEntry(1, 'line one')])
    expect(isFollowing()).toBe(true)

    const scroller = getScroller(container)
    // Park the viewport well above the bottom (scrollHeight 100, clientHeight 50,
    // so bottom is scrollTop 50; 0 is far from bottom).
    Object.defineProperty(scroller, 'scrollTop', { configurable: true, value: 0, writable: true })
    fireEvent.scroll(scroller)

    expect(isFollowing()).toBe(false)
  })

  test('scrolling back to the bottom re-engages follow (no wheel needed)', () => {
    const { container } = renderViewer('live', [makeLogEntry(1, 'line one')])
    const scroller = getScroller(container)

    // Release first.
    Object.defineProperty(scroller, 'scrollTop', { configurable: true, value: 0, writable: true })
    fireEvent.scroll(scroller)
    expect(isFollowing()).toBe(false)

    // Scroll back to bottom (scrollTop 50 == scrollHeight 100 - clientHeight 50).
    Object.defineProperty(scroller, 'scrollTop', { configurable: true, value: 50, writable: true })
    fireEvent.scroll(scroller)
    expect(isFollowing()).toBe(true)
  })

  test('the auto-scroll layout-effect does not itself release follow', () => {
    // The programmatic scrollTop write in the layout effect fires a scroll event;
    // it must NOT be read as a user scroll that releases follow when new entries
    // arrive while pinned to bottom.
    const { rerender } = renderViewer('live', [makeLogEntry(1, 'a')])
    expect(isFollowing()).toBe(true)

    rerender(
      <LogViewer
        entries={[makeLogEntry(1, 'a'), makeLogEntry(2, 'b')]}
        totalBuffered={2}
        stream="all"
        onStreamChange={vi.fn()}
        streamMode="live"
      />,
    )
    expect(isFollowing()).toBe(true)
  })
})

describe('LogViewer status-marker keys do not collide on same-timestamp bursts (FE-SSE-03)', () => {
  test('a crashed -> backoff -> starting burst at one timestamp emits no duplicate-key warning', () => {
    // A rapid state burst can share a single second-granularity timestamp.
    // The bug is the React duplicate-key warning that timestamp-only keys
    // produce — non-deterministic identity that "may cause children to be
    // duplicated and/or omitted." The load-bearing property is key UNIQUENESS,
    // not the render count (React 19 happens to still render all in jsdom, so a
    // count assertion would not bite the bug). We assert the warning is absent.
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    const ts = '2026-04-07T12:00:00Z'
    const entries: StreamEntry[] = [
      { type: 'status', state: 'crashed', timestamp: ts },
      { type: 'status', state: 'backoff', timestamp: ts },
      { type: 'status', state: 'starting', timestamp: ts },
    ]
    renderViewer('live', entries)

    const duplicateKeyWarnings = consoleError.mock.calls.filter((args) =>
      args.some((a) => typeof a === 'string' && a.includes('same key')),
    )
    expect(duplicateKeyWarnings).toHaveLength(0)
    // And all three still render (sanity — the markers are present and distinct).
    expect(screen.getByText('crashed')).toBeInTheDocument()
    expect(screen.getByText('backoff')).toBeInTheDocument()
    expect(screen.getByText('starting')).toBeInTheDocument()
    consoleError.mockRestore()
  })

  test('two identical (state, timestamp) markers emit no duplicate-key warning', () => {
    // Even two identical (state, timestamp) markers must carry distinct keys —
    // the index in the composite key is what disambiguates them.
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    const ts = '2026-04-07T12:00:00Z'
    const entries: StreamEntry[] = [
      { type: 'status', state: 'starting', timestamp: ts },
      { type: 'status', state: 'starting', timestamp: ts },
    ]
    renderViewer('live', entries)

    const duplicateKeyWarnings = consoleError.mock.calls.filter((args) =>
      args.some((a) => typeof a === 'string' && a.includes('same key')),
    )
    expect(duplicateKeyWarnings).toHaveLength(0)
    expect(screen.getAllByText('starting')).toHaveLength(2)
    consoleError.mockRestore()
  })
})

describe('LogViewer buffer-reset disclosure marker (FE-SSE-01)', () => {
  test('renders a louder reset marker with the dropped count', () => {
    const entries: StreamEntry[] = [{ type: 'reset', dropped: 1500 }, makeLogEntry(1, 'fresh line after reset')]
    renderViewer('live', entries)

    // The disclosure text names the loss and the count (toLocaleString -> 1,500).
    expect(screen.getByText(/log buffer reset/i)).toBeInTheDocument()
    expect(screen.getByText(/1,500 earlier lines dropped/i)).toBeInTheDocument()
  })

  test('reset marker renders without a count when dropped is unknown', () => {
    renderViewer('live', [{ type: 'reset', dropped: 0 }])
    const marker = screen.getByText(/log buffer reset/i)
    expect(marker.textContent).toContain('earlier lines dropped')
    expect(marker.textContent).not.toMatch(/\d/)
  })
})
