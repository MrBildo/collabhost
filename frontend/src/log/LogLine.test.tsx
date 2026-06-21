import type { LogEntry } from '@/api/types'
import { render } from '@testing-library/react'
import { afterEach, describe, expect, test, vi } from 'vitest'
import { LogLine } from './LogLine'
import * as parseAnsi from './parse-ansi'

function makeEntry(overrides: Partial<LogEntry> = {}): LogEntry {
  return {
    id: 1,
    timestamp: '2026-04-07T12:00:00Z',
    stream: 'stdout',
    content: 'hello world',
    ...overrides,
  }
}

afterEach(() => {
  vi.restoreAllMocks()
})

describe('LogLine ANSI parse memoization (FE-UI-02)', () => {
  test('does not re-parse ANSI when re-rendered with the same content', () => {
    const spy = vi.spyOn(parseAnsi, 'parseAnsiToSegments')
    const entry = makeEntry({ content: '\x1b[31mERROR\x1b[0m' })

    const { rerender } = render(<LogLine entry={entry} />)
    const callsAfterFirst = spy.mock.calls.length
    expect(callsAfterFirst).toBe(1)

    // Re-render with the same entry reference (the stable case) and a fresh
    // entry object carrying identical content (the new-array-reference case the
    // SSE flush produces). Neither should re-invoke the parser.
    rerender(<LogLine entry={entry} />)
    rerender(<LogLine entry={makeEntry({ content: '\x1b[31mERROR\x1b[0m' })} />)

    expect(spy.mock.calls.length).toBe(callsAfterFirst)
  })

  test('re-parses when the content actually changes', () => {
    const spy = vi.spyOn(parseAnsi, 'parseAnsiToSegments')

    const { rerender } = render(<LogLine entry={makeEntry({ content: 'first' })} />)
    expect(spy.mock.calls.length).toBe(1)

    rerender(<LogLine entry={makeEntry({ content: 'second' })} />)
    expect(spy.mock.calls.length).toBe(2)
  })

  test('renders the parsed content', () => {
    const { container } = render(<LogLine entry={makeEntry({ content: 'plain line' })} />)
    expect(container.textContent).toContain('plain line')
  })
})
