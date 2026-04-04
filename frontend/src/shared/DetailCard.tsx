import { cn } from '@/lib/cn'
import type { ReactNode } from 'react'

type DetailRow = {
  key: string
  label: string
  value: ReactNode
}

type DetailCardProps = {
  title: string
  rows: DetailRow[]
  className?: string
}

function DetailCard({ title, rows, className }: DetailCardProps) {
  return (
    <div className={cn('wm-detail-card', className)}>
      <div className="wm-detail-card__title">{title}</div>
      {rows.map((row) => (
        <div key={row.key} className="wm-detail-row">
          <span className="wm-detail-row__key">{row.label}</span>
          <span className="wm-detail-row__value">{row.value}</span>
        </div>
      ))}
    </div>
  )
}

export { DetailCard }
export type { DetailCardProps, DetailRow }
