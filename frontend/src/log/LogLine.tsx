import type { LogEntry } from '@/api/types'
import { cn } from '@/lib/cn'
import { formatTimestamp } from '@/lib/format'

type LogLineProps = {
  entry: LogEntry
}

const LEVEL_CLASSES: Record<string, string> = {
  INF: 'wm-log-level--info',
  WRN: 'wm-log-level--warn',
  ERR: 'wm-log-level--error',
  HLT: 'wm-log-level--ok',
  UPD: 'wm-log-level--ok',
}

function LogLine({ entry }: LogLineProps) {
  const levelClass = entry.level ? (LEVEL_CLASSES[entry.level] ?? '') : ''

  return (
    <div className="wm-log-line">
      <span className="wm-log-timestamp">{formatTimestamp(entry.timestamp)}</span>
      {entry.level && <span className={cn('wm-log-level', levelClass)}>{entry.level}</span>}
      <span className="wm-log-msg" style={entry.stream === 'stderr' ? { color: 'var(--wm-red)' } : undefined}>
        {entry.content}
      </span>
    </div>
  )
}

export { LogLine }
export type { LogLineProps }
