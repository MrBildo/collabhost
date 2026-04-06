import { cn } from '@/lib/cn'
import type { ReactNode } from 'react'

type ProbePanelProps = {
  title: string
  children: ReactNode
  className?: string
}

function ProbePanel({ title, children, className }: ProbePanelProps) {
  return (
    <div className={cn('wm-probe-panel', className)}>
      <div className="wm-probe-panel__title">{title}</div>
      <div className="wm-probe-panel__body">{children}</div>
    </div>
  )
}

export { ProbePanel }
export type { ProbePanelProps }
