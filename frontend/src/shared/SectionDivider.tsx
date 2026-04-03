import { cn } from '@/lib/cn'
import type { ReactNode } from 'react'

type SectionDividerProps = {
  label: string
  action?: ReactNode
  className?: string
}

function SectionDivider({ label, action, className }: SectionDividerProps) {
  return (
    <div className={cn('wm-section-divider', className)}>
      <span className="wm-section-divider__label">{label}</span>
      <div className="wm-section-divider__line" />
      {action}
    </div>
  )
}

export { SectionDivider }
export type { SectionDividerProps }
