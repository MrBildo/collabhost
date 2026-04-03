import type { LogEntry } from '@/api/types'
import { cn } from '@/lib/cn'
import { FilterChip } from '@/shared/FilterChip'
import { useEffect, useRef, useState } from 'react'
import { LogLine } from './LogLine'

type LogStream = 'all' | 'stdout' | 'stderr'

type LogViewerProps = {
  entries: LogEntry[]
  totalBuffered: number
  stream: LogStream
  onStreamChange: (stream: LogStream) => void
  className?: string
}

const STREAMS: { value: LogStream; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'stdout', label: 'stdout' },
  { value: 'stderr', label: 'stderr' },
]

function LogViewer({ entries, totalBuffered, stream, onStreamChange, className }: LogViewerProps) {
  const [isFollowing, setIsFollowing] = useState(true)
  const scrollRef = useRef<HTMLDivElement>(null)
  const prevEntryCount = useRef(entries.length)

  useEffect(() => {
    if (isFollowing && scrollRef.current && entries.length > prevEntryCount.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
    prevEntryCount.current = entries.length
  }, [entries.length, isFollowing])

  function handleScroll(): void {
    if (!scrollRef.current) return
    const { scrollTop, scrollHeight, clientHeight } = scrollRef.current
    const isAtBottom = scrollHeight - scrollTop - clientHeight < 40
    setIsFollowing(isAtBottom)
  }

  return (
    <div className={cn('flex flex-col', className)}>
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
      <div ref={scrollRef} className="wm-log-viewer flex-1" style={{ minHeight: 300 }} onScroll={handleScroll}>
        {entries.length === 0 ? (
          <div className="text-xs py-4 text-center" style={{ color: 'var(--wm-text-dim)' }}>
            No log entries
          </div>
        ) : (
          entries.map((entry, i) => (
            // Log entries lack unique IDs; index is stable within a polling cycle
            // biome-ignore lint/suspicious/noArrayIndexKey: log entries have no unique key
            <LogLine key={i} entry={entry} />
          ))
        )}
      </div>
    </div>
  )
}

export { LogViewer }
export type { LogViewerProps, LogStream }
