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
    <div className={cn('grid gap-1', className)} style={{ gridTemplateColumns: '140px 1fr' }}>
      <div className="flex flex-col gap-1 pt-2">
        <label
          htmlFor={htmlFor}
          className="text-xs font-medium"
          style={{ color: 'var(--wm-text-dim)', letterSpacing: '0.04em' }}
        >
          {label}
        </label>
        {badges && <div className="flex gap-1 flex-wrap">{badges}</div>}
      </div>
      <div className="flex flex-col gap-1">
        {children}
        {helpText && !error && (
          <span className="text-xs" style={{ color: 'var(--wm-text-dim)', fontSize: '10px' }}>
            {helpText}
          </span>
        )}
        {error && (
          <span className="text-xs" style={{ color: 'var(--wm-red)', fontSize: '10px' }}>
            {error}
          </span>
        )}
      </div>
    </div>
  )
}

export { FormField }
export type { FormFieldProps }
