import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, test, vi } from 'vitest'
import { useLogStream } from './use-log-stream'

// --- MockEventSource ---

type EventSourceListener = (e: MessageEvent) => void

class MockEventSource {
  static instances: MockEventSource[] = []
  static readonly CONNECTING = 0
  static readonly OPEN = 1
  static readonly CLOSED = 2

  url: string
  readyState = 0
  onopen: ((e: Event) => void) | null = null
  onerror: ((e: Event) => void) | null = null
  private listeners = new Map<string, EventSourceListener[]>()

  constructor(url: string) {
    this.url = url
    MockEventSource.instances.push(this)
  }

  addEventListener(type: string, listener: EventSourceListener): void {
    const existing = this.listeners.get(type) ?? []
    existing.push(listener)
    this.listeners.set(type, existing)
  }

  removeEventListener(type: string, listener: EventSourceListener): void {
    const existing = this.listeners.get(type) ?? []
    this.listeners.set(
      type,
      existing.filter((l) => l !== listener),
    )
  }

  close(): void {
    this.readyState = 2
  }

  // Test helpers
  simulateOpen(): void {
    this.readyState = 1
    this.onopen?.(new Event('open'))
  }

  simulateEvent(type: string, data: unknown): void {
    const event = new MessageEvent(type, {
      data: JSON.stringify(data),
      lastEventId:
        type === 'log' && typeof data === 'object' && data !== null && 'id' in data
          ? String((data as { id: number }).id)
          : '',
    })
    for (const listener of this.listeners.get(type) ?? []) {
      listener(event)
    }
  }

  simulateError(): void {
    this.readyState = 2
    this.onerror?.(new Event('error'))
  }
}

// --- Helpers ---

function makeLogEvent(id: number, content = `line ${id}`) {
  return {
    id,
    timestamp: `2026-04-05T12:00:${String(id).padStart(2, '0')}Z`,
    stream: 'stdout' as const,
    content,
    level: 'INF',
  }
}

function latestInstance(): MockEventSource {
  const instance = MockEventSource.instances[MockEventSource.instances.length - 1]
  if (!instance) throw new Error('No MockEventSource instances')
  return instance
}

// --- Setup ---

let rafCallbacks: Array<() => void> = []

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout', 'setInterval', 'clearInterval', 'Date'] })
  MockEventSource.instances = []
  vi.stubGlobal('EventSource', MockEventSource)
  localStorage.setItem('collabhost-user-key', 'test-key-123')

  rafCallbacks = []
  vi.stubGlobal('requestAnimationFrame', (cb: () => void) => {
    rafCallbacks.push(cb)
    return rafCallbacks.length
  })
  vi.stubGlobal('cancelAnimationFrame', vi.fn())
})

afterEach(() => {
  localStorage.clear()
  vi.useRealTimers()
  vi.restoreAllMocks()
})

function flushRaf(): void {
  const cbs = [...rafCallbacks]
  rafCallbacks = []
  for (const cb of cbs) {
    cb()
  }
}

// --- Tests ---

describe('useLogStream', () => {
  test('URL construction includes slug and auth key', () => {
    renderHook(() => useLogStream('my-app'))

    expect(MockEventSource.instances).toHaveLength(1)
    const es = latestInstance()
    expect(es.url).toBe('/api/v1/apps/my-app/logs/stream?key=test-key-123')
  })

  test('reconnect URL includes lastEventId when events have been received', () => {
    renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    // Receive some events to advance maxIdRef
    act(() => {
      es.simulateOpen()
      es.simulateEvent('log', makeLogEvent(1))
      es.simulateEvent('log', makeLogEvent(2))
      es.simulateEvent('log', makeLogEvent(3))
      flushRaf()
    })

    // Trigger reconnect via closed event
    act(() => {
      es.simulateEvent('closed', { reason: 'stopped' })
    })

    // Advance past reconnect delay to create a new EventSource
    act(() => {
      vi.advanceTimersByTime(3000)
    })

    const newEs = latestInstance()
    expect(newEs).not.toBe(es)
    expect(newEs.url).toBe('/api/v1/apps/my-app/logs/stream?key=test-key-123&lastEventId=3')
  })

  test('reconnect URL omits lastEventId on first connection (no prior events)', () => {
    renderHook(() => useLogStream('my-app'))
    const es = latestInstance()
    expect(es.url).toBe('/api/v1/apps/my-app/logs/stream?key=test-key-123')
  })

  test('slug change resets lastEventId in reconnect URL', () => {
    const { rerender } = renderHook(({ slug }: { slug: string }) => useLogStream(slug), {
      initialProps: { slug: 'app-a' },
    })
    const firstEs = latestInstance()

    // Receive events on app-a
    act(() => {
      firstEs.simulateOpen()
      firstEs.simulateEvent('log', makeLogEvent(5))
      flushRaf()
    })

    // Navigate to app-b (slug change resets maxIdRef to 0)
    rerender({ slug: 'app-b' })

    const secondEs = latestInstance()
    // No lastEventId because maxIdRef was reset to 0 on slug change
    expect(secondEs.url).toBe('/api/v1/apps/app-b/logs/stream?key=test-key-123')
  })

  test('log events parsed and accumulated', () => {
    const { result } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
      es.simulateEvent('log', makeLogEvent(1, 'hello'))
      es.simulateEvent('log', makeLogEvent(2, 'world'))
      flushRaf()
    })

    expect(result.current.entries).toHaveLength(2)
    expect(result.current.entries[0]).toEqual({
      type: 'log',
      entry: expect.objectContaining({ id: 1, content: 'hello' }),
    })
    expect(result.current.entries[1]).toEqual({
      type: 'log',
      entry: expect.objectContaining({ id: 2, content: 'world' }),
    })
  })

  test('status events parsed and accumulated', () => {
    const { result } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
      es.simulateEvent('status', { state: 'running', timestamp: '2026-04-05T12:00:00Z' })
      flushRaf()
    })

    expect(result.current.entries).toHaveLength(1)
    expect(result.current.entries[0]).toEqual({
      type: 'status',
      state: 'running',
      timestamp: '2026-04-05T12:00:00Z',
    })
  })

  test('dedup on reconnect skips events with id <= max', () => {
    const { result } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
      for (let i = 1; i <= 5; i++) {
        es.simulateEvent('log', makeLogEvent(i))
      }
      flushRaf()
    })

    expect(result.current.entries).toHaveLength(5)

    // Simulate reconnect burst that overlaps
    act(() => {
      for (let i = 3; i <= 8; i++) {
        es.simulateEvent('log', makeLogEvent(i))
      }
      flushRaf()
    })

    // Should have 8 entries total (ids 1-8), not 11
    const logEntries = result.current.entries.filter((e) => e.type === 'log')
    expect(logEntries).toHaveLength(8)
  })

  test('gap marker inserted when ids are non-contiguous', () => {
    const { result } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
      for (let i = 1; i <= 5; i++) {
        es.simulateEvent('log', makeLogEvent(i))
      }
      flushRaf()
    })

    // Send events starting at 10 (gap of 4)
    act(() => {
      for (let i = 10; i <= 12; i++) {
        es.simulateEvent('log', makeLogEvent(i))
      }
      flushRaf()
    })

    const gaps = result.current.entries.filter((e) => e.type === 'gap')
    expect(gaps).toHaveLength(1)
  })

  test('buffer cap enforced, oldest entries dropped', () => {
    const { result } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
      for (let i = 1; i <= 1100; i++) {
        es.simulateEvent('log', makeLogEvent(i))
      }
      flushRaf()
    })

    expect(result.current.entries.length).toBeLessThanOrEqual(1000)
    // Last entry should have id 1100
    const last = result.current.entries[result.current.entries.length - 1]
    expect(last).toEqual({
      type: 'log',
      entry: expect.objectContaining({ id: 1100 }),
    })
  })

  test('rAF batching collapses multiple events into single render', () => {
    const { result } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    // Send 50 events without flushing rAF
    act(() => {
      es.simulateOpen()
      for (let i = 1; i <= 50; i++) {
        es.simulateEvent('log', makeLogEvent(i))
      }
    })

    // Before rAF flush, render entries should still be empty
    expect(result.current.entries).toHaveLength(0)

    // Only one rAF callback should have been scheduled
    expect(rafCallbacks).toHaveLength(1)

    act(() => {
      flushRaf()
    })

    // After single flush, all 50 should appear
    expect(result.current.entries).toHaveLength(50)
  })

  test('cleanup on unmount closes EventSource', () => {
    const { unmount } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
    })

    expect(es.readyState).toBe(1)

    unmount()

    expect(es.readyState).toBe(2)
  })

  test('cleanup on slug change closes old EventSource and opens new', () => {
    const { rerender } = renderHook(({ slug }: { slug: string }) => useLogStream(slug), {
      initialProps: { slug: 'app-a' },
    })

    const firstEs = latestInstance()
    expect(firstEs.url).toContain('app-a')

    rerender({ slug: 'app-b' })

    expect(firstEs.readyState).toBe(2)
    expect(MockEventSource.instances).toHaveLength(2)
    const secondEs = latestInstance()
    expect(secondEs.url).toContain('app-b')
  })

  test('closed event handling closes EventSource', () => {
    const { result } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
    })

    expect(result.current.isConnected).toBe(true)

    act(() => {
      es.simulateEvent('closed', { reason: 'deleted' })
    })

    expect(es.readyState).toBe(2)
    expect(result.current.isConnected).toBe(false)
  })

  test('error state set on EventSource error', () => {
    const { result } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
    })

    expect(result.current.error).toBeNull()

    act(() => {
      es.simulateError()
    })

    expect(result.current.isConnected).toBe(false)
    expect(result.current.error).toBe('Connection lost')
  })

  test('isConnected tracks EventSource state', () => {
    const { result } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    expect(result.current.isConnected).toBe(false)

    act(() => {
      es.simulateOpen()
    })

    expect(result.current.isConnected).toBe(true)
  })

  test('error recovery on reconnect clears error', () => {
    const { result } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
    })

    act(() => {
      es.simulateError()
    })

    expect(result.current.error).toBe('Connection lost')

    // Advance past reconnect delay to create a new EventSource
    act(() => {
      vi.advanceTimersByTime(3000)
    })

    const newEs = latestInstance()
    expect(newEs).not.toBe(es)

    act(() => {
      newEs.simulateOpen()
    })

    expect(result.current.error).toBeNull()
    expect(result.current.isConnected).toBe(true)
  })

  test('reconnects after closed event and resumes streaming', () => {
    const { result } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    // Initial connection with some log entries
    act(() => {
      es.simulateOpen()
      es.simulateEvent('log', makeLogEvent(1, 'before stop'))
      flushRaf()
    })

    expect(result.current.entries).toHaveLength(1)
    expect(result.current.isStreaming).toBe(true)

    // Backend sends closed event (app stopped)
    act(() => {
      es.simulateEvent('closed', { reason: 'stopped' })
    })

    expect(result.current.isConnected).toBe(false)
    expect(es.readyState).toBe(2)

    // Advance past reconnect delay
    act(() => {
      vi.advanceTimersByTime(3000)
    })

    // A new EventSource should have been created
    expect(MockEventSource.instances).toHaveLength(2)
    const newEs = latestInstance()
    expect(newEs).not.toBe(es)

    // Simulate the new connection opening (app restarted).
    // Same-slug reconnects preserve both entries and maxIdRef, so the
    // history burst is properly deduped against existing entries.
    act(() => {
      newEs.simulateOpen()
      newEs.simulateEvent('log', makeLogEvent(2, 'after start'))
      flushRaf()
    })

    expect(result.current.isConnected).toBe(true)
    expect(result.current.isStreaming).toBe(true)
    // Previous entry (id=1) preserved, new entry (id=2) appended.
    // maxIdRef stayed at 1, so the history burst's id=1 was deduped.
    expect(result.current.entries).toHaveLength(2)
    expect(result.current.entries[0]).toEqual({
      type: 'log',
      entry: expect.objectContaining({ id: 1, content: 'before stop' }),
    })
    expect(result.current.entries[1]).toEqual({
      type: 'log',
      entry: expect.objectContaining({ id: 2, content: 'after start' }),
    })
  })

  test('reconnect timer cleaned up on unmount', () => {
    const { unmount } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
    })

    // Trigger a closed event to schedule reconnect
    act(() => {
      es.simulateEvent('closed', { reason: 'stopped' })
    })

    // Unmount before the reconnect timer fires
    unmount()

    // Advance timers - should not create new EventSource
    act(() => {
      vi.advanceTimersByTime(3000)
    })

    // Only the original EventSource should exist
    expect(MockEventSource.instances).toHaveLength(1)
  })

  test('resetKey change forces EventSource reconnect', () => {
    const { result, rerender } = renderHook(
      ({ resetKey }: { resetKey: string }) => useLogStream('my-app', { resetKey }),
      { initialProps: { resetKey: 'running' } },
    )
    const firstEs = latestInstance()

    act(() => {
      firstEs.simulateOpen()
      firstEs.simulateEvent('log', makeLogEvent(1, 'running log'))
      flushRaf()
    })

    expect(result.current.entries).toHaveLength(1)
    expect(result.current.isConnected).toBe(true)

    // Simulate polled status changing to 'stopped'
    rerender({ resetKey: 'stopped' })

    // Old EventSource should be closed
    expect(firstEs.readyState).toBe(2)

    // A new EventSource should have been created
    const secondEs = latestInstance()
    expect(secondEs).not.toBe(firstEs)

    // New connection opens, gets history burst that overlaps with existing entries
    act(() => {
      secondEs.simulateOpen()
      secondEs.simulateEvent('log', makeLogEvent(1, 'running log'))
      secondEs.simulateEvent('log', makeLogEvent(2, 'new log after restart'))
      flushRaf()
    })

    expect(result.current.isConnected).toBe(true)
    // Same-slug reconnect preserves both entries and maxIdRef (which is 1).
    // History burst: id=1 is deduped (1 <= 1), id=2 passes (2 > 1).
    // Total: 1 preserved + 1 new = 2 entries. No duplicates.
    const logEntries = result.current.entries.filter((e) => e.type === 'log')
    expect(logEntries).toHaveLength(2)
    expect(logEntries[0]).toEqual({
      type: 'log',
      entry: expect.objectContaining({ id: 1, content: 'running log' }),
    })
    expect(logEntries[1]).toEqual({
      type: 'log',
      entry: expect.objectContaining({ id: 2, content: 'new log after restart' }),
    })
  })

  test('liveness check detects silently dead connection and reconnects', () => {
    const { result } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
      es.simulateEvent('log', makeLogEvent(1, 'hello'))
      flushRaf()
    })

    expect(result.current.isConnected).toBe(true)

    // Simulate time passing without any events (connection silently dead).
    // The liveness check runs every 10s with a 45s timeout.
    // Advance 50s to trigger at least one check past the timeout.
    act(() => {
      vi.advanceTimersByTime(50_000)
    })

    // The liveness check should have closed the old EventSource
    expect(es.readyState).toBe(2)
    expect(result.current.isConnected).toBe(false)

    // After reconnect delay, a new EventSource is created
    act(() => {
      vi.advanceTimersByTime(3_000)
    })

    expect(MockEventSource.instances.length).toBeGreaterThan(1)
    const newEs = latestInstance()
    expect(newEs).not.toBe(es)

    // New connection opens — same-slug reconnect preserves entries and maxIdRef
    act(() => {
      newEs.simulateOpen()
      newEs.simulateEvent('log', makeLogEvent(2, 'reconnected'))
      flushRaf()
    })

    expect(result.current.isConnected).toBe(true)
    // Previous entry (id=1) preserved, new entry (id=2) appended.
    // maxIdRef stayed at 1, so id=2 passes dedup naturally.
    expect(result.current.entries).toHaveLength(2)
    expect(result.current.entries[0]).toEqual({
      type: 'log',
      entry: expect.objectContaining({ id: 1, content: 'hello' }),
    })
    expect(result.current.entries[1]).toEqual({
      type: 'log',
      entry: expect.objectContaining({ id: 2, content: 'reconnected' }),
    })
  })

  test('liveness check does not fire when events are flowing', () => {
    renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
    })

    // Send events periodically, keeping the connection alive
    for (let i = 1; i <= 5; i++) {
      act(() => {
        vi.advanceTimersByTime(10_000)
        es.simulateEvent('log', makeLogEvent(i))
        flushRaf()
      })
    }

    // After 50 seconds of regular events, only one EventSource should exist
    expect(MockEventSource.instances).toHaveLength(1)
    expect(es.readyState).toBe(1)
  })

  test('liveness interval cleaned up on unmount', () => {
    const { unmount } = renderHook(() => useLogStream('my-app'))
    const es = latestInstance()

    act(() => {
      es.simulateOpen()
      es.simulateEvent('log', makeLogEvent(1))
      flushRaf()
    })

    unmount()

    // Advance time well past the liveness timeout
    act(() => {
      vi.advanceTimersByTime(60_000)
    })

    // No new EventSource instances should have been created after unmount
    expect(MockEventSource.instances).toHaveLength(1)
  })
})
