import type { DashboardEvent } from '@/api/types'
import { cn } from '@/lib/cn'
import { formatTimestamp } from '@/lib/format'

type EventListProps = {
  events: DashboardEvent[]
  className?: string
}

const SEVERITY_STYLES: Record<string, string | undefined> = {
  error: 'var(--wm-red)',
  warning: 'var(--wm-yellow)',
  info: undefined,
}

function EventList({ events, className }: EventListProps) {
  if (events.length === 0) {
    return (
      <div className={cn('wm-panel p-4 text-center', className)}>
        <span className="text-xs" style={{ color: 'var(--wm-text-dim)' }}>
          No recent events
        </span>
      </div>
    )
  }

  return (
    <div className={cn('wm-panel overflow-hidden', className)}>
      {events.map((event, i) => {
        const msgColor = SEVERITY_STYLES[event.severity]
        return (
          <div
            // biome-ignore lint/suspicious/noArrayIndexKey: events lack unique IDs
            key={i}
            className="flex items-center gap-3 px-3.5 py-2"
            style={{ borderBottom: i < events.length - 1 ? '1px solid rgba(42, 42, 42, 0.5)' : undefined }}
          >
            <span
              className="text-xs flex-shrink-0"
              style={{ color: 'var(--wm-text-dim)', fontVariantNumeric: 'tabular-nums', minWidth: 64 }}
            >
              {formatTimestamp(event.timestamp)}
            </span>
            <span className="text-xs flex-1" style={msgColor ? { color: msgColor } : undefined}>
              {event.appName && (
                <strong style={{ fontWeight: 600, color: msgColor ?? 'var(--wm-text-bright)' }}>{event.appName}</strong>
              )}{' '}
              {event.message}
            </span>
            <span className="wm-type-badge flex-shrink-0">{event.source}</span>
          </div>
        )
      })}
    </div>
  )
}

export { EventList }
export type { EventListProps }
