import { cn } from '@/lib/cn'

type TextFieldProps = {
  id?: string
  value: string
  onChange: (value: string) => void
  placeholder?: string
  disabled?: boolean
  readOnly?: boolean
  hasError?: boolean
  className?: string
}

function TextField({ id, value, onChange, placeholder, disabled, readOnly, hasError, className }: TextFieldProps) {
  return (
    <input
      id={id}
      type="text"
      className={cn('wm-input', hasError && 'wm-input--error', readOnly && 'wm-input--readonly', className)}
      value={value}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      disabled={disabled}
      readOnly={readOnly}
    />
  )
}

export { TextField }
export type { TextFieldProps }
