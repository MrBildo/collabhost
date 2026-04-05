import type { AppStatus, StreamEntry } from '@/api/types'
import { cn } from '@/lib/cn'
import { FilterChip } from '@/shared/FilterChip'
import { useEffect, useRef, useState } from 'react'
import { LogLine } from './LogLine'

type LogStream = 'all' | 'stdout' | 'stderr'

type LogViewerProps = {
  entries: StreamEntry[]
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

function LogViewer({ entries, totalBuffered, stream, onStreamChange, className }: LogViewerProps) {
  const [isFollowing, setIsFollowing] = useState(true)
  const scrollRef = useRef<HTMLDivElement>(null)
  const prevEntryCount = useRef(entries.length)

  const filteredEntries =
    stream === 'all' ? entries : entries.filter((item) => item.type !== 'log' || item.entry.stream === stream)

  useEffect(() => {
    if (isFollowing && scrollRef.current && filteredEntries.length > prevEntryCount.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight
    }
    prevEntryCount.current = filteredEntries.length
  }, [filteredEntries.length, isFollowing])

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
        {filteredEntries.length === 0 ? (
          <div className="text-xs py-4 text-center" style={{ color: 'var(--wm-text-dim)' }}>
            No log entries
          </div>
        ) : (
          filteredEntries.map((item, i) => {
            if (item.type === 'log') {
              return <LogLine key={item.entry.id ?? i} entry={item.entry} />
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
export type { LogViewerProps, LogStream }
