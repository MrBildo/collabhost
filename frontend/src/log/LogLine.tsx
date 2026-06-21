import type { LogEntry } from '@/api/types'
import { cn } from '@/lib/cn'
import { formatTimestamp } from '@/lib/format'
import { memo, useMemo } from 'react'
import type { AnsiSegment } from './parse-ansi'
import { parseAnsiToSegments } from './parse-ansi'

type LogLineProps = {
  entry: LogEntry
}

type LogContentProps = {
  content: string
  isStderr: boolean
}

const LEVEL_CLASSES: Record<string, string> = {
  INF: 'wm-log-level--info',
  WRN: 'wm-log-level--warn',
  ERR: 'wm-log-level--error',
  DBG: 'wm-log-level--debug',
  FTL: 'wm-log-level--fatal',
  HLT: 'wm-log-level--ok',
  UPD: 'wm-log-level--ok',
}

function LogContent({ content, isStderr }: LogContentProps) {
  // Memoize the ANSI parse on `content`. The log viewer creates a new `entries`
  // array reference on every SSE flush, re-rendering the whole list; without
  // this, every visible line re-parses its ANSI on each flush — O(buffer) work
  // per frame while streaming. Keyed on `content` only (the parse's sole input).
  const segments: AnsiSegment[] = useMemo(() => parseAnsiToSegments(content), [content])
  const isPlain = segments.every((s) => !s.color && !s.bold && !s.dim)

  if (isPlain) {
    const text = segments.map((s) => s.text).join('')
    return (
      <span className="wm-log-msg" style={isStderr ? { color: 'var(--wm-red)' } : undefined}>
        {text}
      </span>
    )
  }

  return (
    <span className="wm-log-msg">
      {segments.map((seg, i) => (
        <span
          // biome-ignore lint/suspicious/noArrayIndexKey: ANSI segments are positional, no stable key available
          key={i}
          style={{
            color: seg.color ? `var(${seg.color})` : isStderr ? 'var(--wm-red)' : undefined,
            fontWeight: seg.bold ? 700 : undefined,
            opacity: seg.dim ? 0.6 : undefined,
          }}
        >
          {seg.text}
        </span>
      ))}
    </span>
  )
}

function LogLineComponent({ entry }: LogLineProps) {
  const levelClass = entry.level ? (LEVEL_CLASSES[entry.level] ?? '') : ''

  return (
    <div className="wm-log-line">
      <span className="wm-log-timestamp">{formatTimestamp(entry.timestamp)}</span>
      {entry.level && <span className={cn('wm-log-level', levelClass)}>{entry.level}</span>}
      <LogContent content={entry.content} isStderr={entry.stream === 'stderr'} />
    </div>
  )
}

// memo: a log entry is immutable once received (keyed by a monotonic id), but a
// new SSE flush hands the viewer a fresh `entries` array, re-rendering every
// child. Memoizing on the `entry` reference short-circuits re-render for lines
// whose entry object did not change — the streaming-hot-path optimization for
// FE-UI-02, paired with the per-line ANSI parse memo in LogContent.
const LogLine = memo(LogLineComponent)

export { LogLine }
export type { LogLineProps }
