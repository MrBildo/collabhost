import type { LogEntry } from '@/api/types'
import { cn } from '@/lib/cn'
import { formatTimestamp } from '@/lib/format'
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
  const segments: AnsiSegment[] = parseAnsiToSegments(content)
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

function LogLine({ entry }: LogLineProps) {
  const levelClass = entry.level ? (LEVEL_CLASSES[entry.level] ?? '') : ''

  return (
    <div className="wm-log-line">
      <span className="wm-log-timestamp">{formatTimestamp(entry.timestamp)}</span>
      {entry.level && <span className={cn('wm-log-level', levelClass)}>{entry.level}</span>}
      <LogContent content={entry.content} isStderr={entry.stream === 'stderr'} />
    </div>
  )
}

export { LogLine }
export type { LogLineProps }
