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

// Buffer-overflow disclosure (FE-SSE-01). A reset drops an unrecoverable range
// larger than the buffer — a bigger loss than a 'gap', so it reads LOUDER:
// amber (vs the gap marker's dimmed grey) and a solid rule, with the dropped
// count when known. Fixes the bigger-loss-quieter-UI inversion.
function LogResetMarker({ dropped }: { dropped: number }) {
  return (
    <div className="flex items-center gap-2 py-1" style={{ color: 'var(--wm-amber)' }}>
      <div className="flex-1" style={{ borderTop: '1px solid var(--wm-amber-border)' }} />
      <span className="text-xs" style={{ fontStyle: 'italic' }}>
        {dropped > 0
          ? `log buffer reset — ${dropped.toLocaleString()} earlier lines dropped`
          : 'log buffer reset — earlier lines dropped'}
      </span>
      <div className="flex-1" style={{ borderTop: '1px solid var(--wm-amber-border)' }} />
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

// Tolerance (px) for treating a scroll position as "at the bottom" — covers
// sub-pixel rounding and the small slack a user expects when near the end.
const AT_BOTTOM_THRESHOLD_PX = 40

function LogViewer({ entries, totalBuffered, stream, onStreamChange, streamMode, className }: LogViewerProps) {
  const [isFollowing, setIsFollowing] = useState(true)
  const scrollRef = useRef<HTMLDivElement>(null)
  // The scrollTop our own auto-scroll last wrote. handleScroll compares against
  // it to tell our programmatic scroll apart from genuine user scrolls — without
  // relying on a scroll event actually firing (it may not, e.g. a no-op write or
  // jsdom), which a one-shot boolean flag would leak into the next user scroll.
  const programmaticScrollTopRef = useRef<number | null>(null)

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
      const el = scrollRef.current
      // The browser clamps scrollTop to (scrollHeight - clientHeight); record the
      // clamped landing position (not scrollHeight) so handleScroll recognizes the
      // resulting scroll event as our own rather than user intent (FE-UI-01).
      const target = Math.max(0, el.scrollHeight - el.clientHeight)
      programmaticScrollTopRef.current = target
      el.scrollTop = target
    }
  }, [entries, isFollowing])

  // Any user scroll — wheel, keyboard (PageUp/arrows), scrollbar drag, or touch —
  // arrives as a `scroll` event. The earlier wheel-only guard (FE-UI-01) left an
  // un-releasable auto-scroller for every non-wheel input device. We now treat
  // every scroll as user intent EXCEPT the one our own layout-effect just wrote.
  function handleScroll(): void {
    if (!scrollRef.current) return
    const { scrollTop, scrollHeight, clientHeight } = scrollRef.current

    // Our own auto-scroll landed here — consume it, don't read it as user intent.
    if (programmaticScrollTopRef.current !== null && Math.abs(scrollTop - programmaticScrollTopRef.current) < 1) {
      programmaticScrollTopRef.current = null
      return
    }
    programmaticScrollTopRef.current = null

    const isAtBottom = scrollHeight - scrollTop - clientHeight < AT_BOTTOM_THRESHOLD_PX
    setIsFollowing(isAtBottom)
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
      <div ref={scrollRef} className="wm-log-viewer flex-1" onScroll={handleScroll}>
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
              // Composite key: a status burst (crashed -> backoff -> starting) can
              // share one timestamp at second granularity, so timestamp alone
              // collides and React warns + may drop markers. Fold in the position
              // and state so co-timestamped markers stay distinct (FE-SSE-03).
              return <LogStatusMarker key={`status-${i}-${item.state}-${item.timestamp}`} state={item.state} />
            }
            if (item.type === 'gap') {
              // biome-ignore lint/suspicious/noArrayIndexKey: gap markers are synthetic with no stable identity
              return <LogGapMarker key={`gap-${i}`} />
            }
            if (item.type === 'reset') {
              // biome-ignore lint/suspicious/noArrayIndexKey: reset markers are synthetic with no stable identity
              return <LogResetMarker key={`reset-${i}`} dropped={item.dropped} />
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
