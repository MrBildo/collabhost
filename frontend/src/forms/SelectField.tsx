import type { FieldOption } from '@/api/types'
import { cn } from '@/lib/cn'

type SelectFieldProps = {
  id?: string
  value: string
  onChange: (value: string) => void
  options: FieldOption[]
  disabled?: boolean
  hasError?: boolean
  className?: string
}

function SelectField({ id, value, onChange, options, disabled, hasError, className }: SelectFieldProps) {
  return (
    <select
      id={id}
      className={cn('wm-select', hasError && 'wm-input--error', className)}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      disabled={disabled}
    >
      {options.map((opt) => (
        <option key={opt.value} value={opt.value}>
          {opt.label}
        </option>
      ))}
    </select>
  )
}

export { SelectField }
export type { SelectFieldProps }
