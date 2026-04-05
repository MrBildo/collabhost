import { cn } from '@/lib/cn'
import type { ReactNode } from 'react'

type FormFieldProps = {
  label: string
  htmlFor?: string
  helpText?: string
  error?: string
  badges?: ReactNode
  children: ReactNode
  className?: string
}

function FormField({ label, htmlFor, helpText, error, badges, children, className }: FormFieldProps) {
  return (
    <div className={cn('grid gap-x-4', className)} style={{ gridTemplateColumns: '200px 1fr' }}>
      <div className="flex items-start" style={{ paddingTop: '6px' }}>
        <label
          htmlFor={htmlFor}
          className="text-xs font-medium"
          style={{ color: 'var(--wm-text-dim)', letterSpacing: '0.04em' }}
        >
          {label}
        </label>
      </div>
      <div className="flex flex-col gap-1">
        <div className="flex items-center gap-2">
          <div className="flex-1">{children}</div>
          {badges && <div className="flex gap-1 flex-wrap">{badges}</div>}
        </div>
        {helpText && !error && (
          <span className="text-xs" style={{ color: 'var(--wm-text-dim)', fontSize: '12px' }}>
            {helpText}
          </span>
        )}
        {error && (
          <span className="text-xs" style={{ color: 'var(--wm-red)', fontSize: '12px' }}>
            {error}
          </span>
        )}
      </div>
    </div>
  )
}

export { FormField }
export type { FormFieldProps }
