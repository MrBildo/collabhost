import { cn } from '@/lib/cn'

type StatusCell = {
  label: string
  value: string | number
  detail?: string
  color?: 'amber' | 'green' | 'red' | 'default'
}

type StatusStripProps = {
  cells: StatusCell[]
  className?: string
}

const COLOR_CLASSES: Record<string, string> = {
  amber: 'wm-status-cell__value--amber',
  green: 'wm-status-cell__value--green',
  red: 'wm-status-cell__value--red',
  default: '',
}

function StatusStrip({ cells, className }: StatusStripProps) {
  return (
    <div className={cn('wm-status-strip', className)}>
      {cells.map((cell) => (
        <div key={cell.label} className="wm-status-cell">
          <div className="wm-status-cell__label">{cell.label}</div>
          <div className={cn('wm-status-cell__value', COLOR_CLASSES[cell.color ?? 'default'])}>{cell.value}</div>
          {cell.detail && <div className="wm-status-cell__detail">{cell.detail}</div>}
        </div>
      ))}
    </div>
  )
}

export { StatusStrip }
export type { StatusStripProps, StatusCell }
