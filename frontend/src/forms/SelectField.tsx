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
  // Normalize value to match option casing (backend may send PascalCase, options use camelCase)
  const normalizedValue = options.find((o) => o.value.toLowerCase() === value.toLowerCase())?.value ?? value

  return (
    <select
      id={id}
      className={cn('wm-select', hasError && 'wm-input--error', className)}
      value={normalizedValue}
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
