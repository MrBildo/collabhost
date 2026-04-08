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

function EventList({ events, className }: EventListProps) {
  const [isFollowing, setIsFollowing] = useState(true)
  const scrollRef = useRef<HTMLDivElement>(null)
  const userScrolledRef = useRef(false)

  // useLayoutEffect runs after DOM mutations but before paint — same
  // pattern as LogViewer. Depend on events reference (not length) so
  // follow works even at buffer cap when eviction keeps length stable.
  // biome-ignore lint/correctness/useExhaustiveDependencies: events reference is an intentional re-trigger signal for auto-scroll on new data
  useLayoutEffect(() => {
    if (isFollowing && scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
  }, [events, isFollowing])

  function handleScroll(): void {
    if (!scrollRef.current) return
    const { scrollTop, scrollHeight, clientHeight } = scrollRef.current
    const isAtBottom = scrollHeight - scrollTop - clientHeight < 40

    // Only update follow state from user-initiated scrolls, not from
    // our own programmatic scrollTop assignment in the layout effect.
    if (userScrolledRef.current) {
      setIsFollowing(isAtBottom)
      userScrolledRef.current = false
    }
  }

  function handleWheel(): void {
    userScrolledRef.current = true
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
      <div ref={scrollRef} className="wm-event-list" onScroll={handleScroll} onWheel={handleWheel}>
        {events.length === 0 ? (
          <div className="flex items-center justify-center h-full">
            <span className="text-xs" style={{ color: 'var(--wm-text-dim)' }}>
              No recent events
            </span>
          </div>
        ) : (
          events.map((event, i) => {
            const msgColor = SEVERITY_STYLES[event.severity]
            return (
              <div
                // biome-ignore lint/suspicious/noArrayIndexKey: events lack unique IDs
                key={i}
                className="flex items-center gap-3 px-3.5 py-2"
                style={{ borderBottom: i < events.length - 1 ? '1px solid var(--wm-border-subtle)' : undefined }}
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
