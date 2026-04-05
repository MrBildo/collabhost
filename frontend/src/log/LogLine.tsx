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
  DBG: 'wm-log-level--debug',
  FTL: 'wm-log-level--fatal',
  HLT: 'wm-log-level--ok',
  UPD: 'wm-log-level--ok',
}

// Strip ANSI escape sequences (terminal color codes, cursor movement, etc.)
// These appear in raw stdout from tools like .NET's console logger.
// biome-ignore lint/suspicious/noControlCharactersInRegex: ANSI escape codes use control characters by definition
const ANSI_RE = /\x1b\[[0-9;]*[A-Za-z]|\x1b\].*?(?:\x07|\x1b\\)/g

function stripAnsi(text: string): string {
  return text.replace(ANSI_RE, '')
}

function LogLine({ entry }: LogLineProps) {
  const levelClass = entry.level ? (LEVEL_CLASSES[entry.level] ?? '') : ''

  return (
    <div className="wm-log-line">
      <span className="wm-log-timestamp">{formatTimestamp(entry.timestamp)}</span>
      {entry.level && <span className={cn('wm-log-level', levelClass)}>{entry.level}</span>}
      <span className="wm-log-msg" style={entry.stream === 'stderr' ? { color: 'var(--wm-red)' } : undefined}>
        {stripAnsi(entry.content)}
      </span>
    </div>
  )
}

export { LogLine }
export type { LogLineProps }
