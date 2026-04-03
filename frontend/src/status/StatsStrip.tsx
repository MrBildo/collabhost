import { cn } from '@/lib/cn'

type StatItem = {
  label: string
  value: string | number
  color?: 'green' | 'red' | 'amber' | 'default'
}

type StatsStripProps = {
  items: StatItem[]
  className?: string
}

const COLOR_STYLES: Record<string, string | undefined> = {
  green: 'var(--wm-green)',
  red: 'var(--wm-red)',
  amber: 'var(--wm-amber)',
  default: undefined,
}

function StatsStrip({ items, className }: StatsStripProps) {
  return (
    <div className={cn('wm-status-strip', className)}>
      {items.map((item) => (
        <div key={item.label} className="wm-status-cell">
          <div className="wm-status-cell__label">{item.label}</div>
          <div
            className="wm-status-cell__value"
            style={item.color && item.color !== 'default' ? { color: COLOR_STYLES[item.color] } : undefined}
          >
            {item.value}
          </div>
        </div>
      ))}
    </div>
  )
}

export { StatsStrip }
export type { StatsStripProps, StatItem }
