import type { DashboardEvent } from '@/api/types'
import { cn } from '@/lib/cn'
import { formatTimestamp } from '@/lib/format'
import { useLayoutEffect, useRef, useState } from 'react'

type EventListProps = {
  events: DashboardEvent[]
  className?: string
}

const SEVERITY_STYLES: Record<DashboardEvent['severity'], string | undefined> = {
  error: 'var(--wm-red)',
  warning: 'var(--wm-yellow)',
  info: undefined,
}

// Tolerance (px) for treating a scroll position as "at the bottom" — covers
// sub-pixel rounding and the small slack a user expects near the end.
const AT_BOTTOM_THRESHOLD_PX = 40

function EventList({ events, className }: EventListProps) {
  const [isFollowing, setIsFollowing] = useState(true)
  const scrollRef = useRef<HTMLDivElement>(null)
  // The scrollTop our own auto-scroll last wrote — see LogViewer for the full
  // rationale. Compared against in handleScroll so our programmatic scroll is not
  // mistaken for user intent (FE-UI-01); a one-shot boolean would leak.
  const programmaticScrollTopRef = useRef<number | null>(null)

  // Reverse to chronological order (oldest first, newest at bottom) so
  // new events appear at the bottom — same mental model as the log viewer.
  // The API returns newest-first; we flip for consistent time-flows-down UX.
  const chronological = [...events].reverse()

  // useLayoutEffect runs after DOM mutations but before paint — same
  // pattern as LogViewer. Depend on events reference (not length) so
  // follow works even at buffer cap when eviction keeps length stable.
  // biome-ignore lint/correctness/useExhaustiveDependencies: events reference is an intentional re-trigger signal for auto-scroll on new data
  useLayoutEffect(() => {
    if (isFollowing && scrollRef.current) {
      const el = scrollRef.current
      const target = Math.max(0, el.scrollHeight - el.clientHeight)
      programmaticScrollTopRef.current = target
      el.scrollTop = target
    }
  }, [events, isFollowing])

  // Release follow on any user scroll (wheel, keyboard, scrollbar, touch), not
  // only wheel (FE-UI-01). Every scroll is user intent except the one our own
  // layout-effect just wrote.
  function handleScroll(): void {
    if (!scrollRef.current) return
    const { scrollTop, scrollHeight, clientHeight } = scrollRef.current

    if (programmaticScrollTopRef.current !== null && Math.abs(scrollTop - programmaticScrollTopRef.current) < 1) {
      programmaticScrollTopRef.current = null
      return
    }
    programmaticScrollTopRef.current = null

    const isAtBottom = scrollHeight - scrollTop - clientHeight < AT_BOTTOM_THRESHOLD_PX
    setIsFollowing(isAtBottom)
  }

  return (
    <div className={cn('flex flex-col', className)}>
      <div className="flex items-center justify-end mb-2">
        <button
          type="button"
          className={cn('wm-filter-chip', isFollowing && 'wm-filter-chip--active')}
          onClick={() => setIsFollowing(!isFollowing)}
        >
          Follow
        </button>
      </div>
      <div ref={scrollRef} className="wm-event-list" onScroll={handleScroll}>
        {chronological.length === 0 ? (
          <div className="flex items-center justify-center h-full">
            <span className="text-xs" style={{ color: 'var(--wm-text-dim)' }}>
              No recent events
            </span>
          </div>
        ) : (
          chronological.map((event, i) => {
            const msgColor = SEVERITY_STYLES[event.severity]
            return (
              <div
                key={event.id}
                className="flex items-center gap-3 px-3.5 py-2"
                style={{
                  borderBottom: i < chronological.length - 1 ? '1px solid var(--wm-border-subtle)' : undefined,
                }}
              >
                <span
                  className="text-xs flex-shrink-0"
                  style={{ color: 'var(--wm-text-dim)', fontVariantNumeric: 'tabular-nums', minWidth: 64 }}
                >
                  {formatTimestamp(event.timestamp)}
                </span>
                <span className="text-xs flex-1" style={msgColor ? { color: msgColor } : undefined}>
                  {event.appSlug && (
                    <strong style={{ fontWeight: 600, color: msgColor ?? 'var(--wm-text-bright)' }}>
                      {event.appSlug}
                    </strong>
                  )}{' '}
                  {event.message}
                </span>
                <span className="wm-type-badge flex-shrink-0">{event.source}</span>
              </div>
            )
          })
        )}
      </div>
    </div>
  )
}

export { EventList }
export type { EventListProps }
