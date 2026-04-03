import type { ReactNode } from 'react'
import { Link } from 'react-router-dom'

type BreadcrumbSegment = {
  label: string
  to?: string
}

type BreadcrumbsProps = {
  segments: BreadcrumbSegment[]
  actions?: ReactNode
}

function Breadcrumbs({ segments, actions }: BreadcrumbsProps) {
  return (
    <div
      className="flex items-center justify-between py-2.5 mb-4"
      style={{ borderBottom: '1px solid var(--wm-border)' }}
    >
      <nav className="wm-breadcrumb flex items-center gap-1.5">
        {segments.map((segment, i) => {
          const isLast = i === segments.length - 1
          return (
            <span key={segment.label} className="flex items-center gap-1.5">
              {i > 0 && <span className="wm-breadcrumb__separator">/</span>}
              {isLast || !segment.to ? (
                <span className={isLast ? 'wm-breadcrumb__current' : 'wm-breadcrumb__link'}>{segment.label}</span>
              ) : (
                <Link to={segment.to} className="wm-breadcrumb__link">
                  {segment.label}
                </Link>
              )}
            </span>
          )
        })}
      </nav>
      {actions && <div className="flex items-center gap-2">{actions}</div>}
    </div>
  )
}

export { Breadcrumbs }
export type { BreadcrumbsProps, BreadcrumbSegment }
