import type { AppStatus, StreamEntry } from '@/api/types'
import { cn } from '@/lib/cn'
import { FilterChip } from '@/shared/FilterChip'
import { useLayoutEffect, useRef, useState } from 'react'
import { LogLine } from './LogLine'

type LogStream = 'all' | 'stdout' | 'stderr'

/**
 * Connection mode for the log feed:
 * - 'live'         — SSE connection is open and streaming (the healthy path)
 * - 'polling'      — SSE never connected or yielded no entries; the page is
 *                    polling /logs as the documented fallback
 * - 'reconnecting' — SSE produced entries then dropped; entries are stale
 *                    while the hook is reconnecting (the ~45s "neither mode"
 *                    window — liveness-net bounded). (#321)
 *
 * Both non-'live' modes render a small degraded-mode indicator so the operator
 * has a signal that the feed isn't live. Bill's ruling on #321.
 */
type StreamMode = 'live' | 'polling' | 'reconnecting'

type LogViewerProps = {
  entries: StreamEntry[]
  totalBuffered: number
  stream: LogStream
  onStreamChange: (stream: LogStream) => void
  streamMode: StreamMode
  className?: string
}

const STREAMS: { value: LogStream; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'stdout', label: 'stdout' },
  { value: 'stderr', label: 'stderr' },
]

function LogStatusMarker({ state }: { state: AppStatus }) {
  return (
    <div className="flex items-center gap-2 py-1" style={{ color: 'var(--wm-text-dim)', opacity: 0.5 }}>
      <div className="flex-1" style={{ borderTop: '1px solid var(--wm-border-subtle)' }} />
      <span className="text-xs" style={{ fontStyle: 'italic' }}>
        {state}
      </span>
      <div className="flex-1" style={{ borderTop: '1px solid var(--wm-border-subtle)' }} />
    </div>
  )
}

function LogGapMarker() {
  return (
    <div className="flex items-center gap-2 py-1" style={{ color: 'var(--wm-text-dim)', opacity: 0.4 }}>
      <div className="flex-1" style={{ borderTop: '1px dashed var(--wm-border-subtle)' }} />
      <span className="text-xs">connection gap</span>
      <div className="flex-1" style={{ borderTop: '1px dashed var(--wm-border-subtle)' }} />
    </div>
  )
}

function StreamModeIndicator({ mode }: { mode: StreamMode }) {
  if (mode === 'live') return null
  const label = mode === 'polling' ? 'polling' : 'reconnecting'
  return (
    <span
      className="wm-stream-mode"
      data-mode={mode}
      data-testid="stream-mode-indicator"
      title={
        mode === 'polling'
          ? 'Live stream unavailable — polling for log updates'
          : 'Live stream interrupted — reconnecting'
      }
    >
      <span className="wm-stream-mode__dot" aria-hidden="true" />
      <span className="wm-stream-mode__label">{label}</span>
    </span>
  )
}

function LogViewer({ entries, totalBuffered, stream, onStreamChange, streamMode, className }: LogViewerProps) {
  const [isFollowing, setIsFollowing] = useState(true)
  const scrollRef = useRef<HTMLDivElement>(null)
  const userScrolledRef = useRef(false)

  const filteredEntries =
    stream === 'all' ? entries : entries.filter((item) => item.type !== 'log' || item.entry.stream === stream)

  // useLayoutEffect runs after DOM mutations but before paint, so the
  // scroll container already has the new elements measured. When Follow
  // is on, pin to bottom every time entries change.
  //
  // We depend on the `entries` prop reference (not length) because the
  // log stream hook creates a new array reference on each flush. Using
  // length breaks at buffer cap: eviction keeps length at ~1000 so the
  // effect never re-fires. The entries reference changes exactly when
  // new data arrives, which is the right trigger regardless of eviction.
  // biome-ignore lint/correctness/useExhaustiveDependencies: entries reference is an intentional re-trigger signal for auto-scroll on new data
  useLayoutEffect(() => {
    if (isFollowing && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
  }, [entries, isFollowing])

  function handleScroll(): void {
    if (!scrollRef.current) return
    const { scrollTop, scrollHeight, clientHeight } = scrollRef.current
    const isAtBottom = scrollHeight - scrollTop - clientHeight < 40

    // Only auto-update follow state from user-initiated scrolls, not
    // from our programmatic scrollTop assignment in the layout effect.
    if (userScrolledRef.current) {
      setIsFollowing(isAtBottom)
      userScrolledRef.current = false
    }
  }

  function handleWheel(): void {
    userScrolledRef.current = true
  }

  return (
    <div className={cn('flex flex-col min-h-0', className)}>
      <div className="flex items-center justify-between mb-2">
        <div className="flex items-center gap-1.5">
          {STREAMS.map((s) => (
            <FilterChip
              key={s.value}
              label={s.label}
              isActive={stream === s.value}
              onClick={() => onStreamChange(s.value)}
            />
          ))}
        </div>
        <div className="flex items-center gap-3">
          <StreamModeIndicator mode={streamMode} />
          <span className="text-xs" style={{ color: 'var(--wm-text-dim)' }}>
            {totalBuffered} buffered
          </span>
          <button
            type="button"
            className={cn('wm-filter-chip', isFollowing && 'wm-filter-chip--active')}
            onClick={() => setIsFollowing(!isFollowing)}
          >
            Follow
          </button>
        </div>
      </div>
      <div ref={scrollRef} className="wm-log-viewer flex-1" onScroll={handleScroll} onWheel={handleWheel}>
        {filteredEntries.length === 0 ? (
          <div className="text-xs py-4 text-center" style={{ color: 'var(--wm-text-dim)' }}>
            No log entries
          </div>
        ) : (
          filteredEntries.map((item, i) => {
            if (item.type === 'log') {
              return <LogLine key={item.entry.id} entry={item.entry} />
            }
            if (item.type === 'status') {
              return <LogStatusMarker key={`status-${item.timestamp}`} state={item.state} />
            }
            if (item.type === 'gap') {
              // biome-ignore lint/suspicious/noArrayIndexKey: gap markers are synthetic with no stable identity
              return <LogGapMarker key={`gap-${i}`} />
            }
            return null
          })
        )}
      </div>
    </div>
  )
}

export { LogViewer }
export type { LogViewerProps, LogStream, StreamMode }
