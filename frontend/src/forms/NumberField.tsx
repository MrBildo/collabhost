import { cn } from '@/lib/cn'

type NumberFieldProps = {
  id?: string
  value: number | null
  onChange: (value: number | null) => void
  placeholder?: string
  disabled?: boolean
  readOnly?: boolean
  hasError?: boolean
  unit?: string
  className?: string
}

function NumberField({
  id,
  value,
  onChange,
  placeholder,
  disabled,
  readOnly,
  hasError,
  unit,
  className,
}: NumberFieldProps) {
  function handleChange(raw: string): void {
    if (raw === '') {
      onChange(null)
      return
    }
    const parsed = Number(raw)
    if (!Number.isNaN(parsed)) {
      onChange(parsed)
    }
  }

  return (
    <div className="flex items-center gap-2">
      <input
        id={id}
        type="number"
        className={cn('wm-input', hasError && 'wm-input--error', readOnly && 'wm-input--readonly', className)}
        value={value ?? ''}
        onChange={(e) => handleChange(e.target.value)}
        placeholder={placeholder}
        disabled={disabled}
        readOnly={readOnly}
      />
      {unit && (
        <span className="text-xs flex-shrink-0" style={{ color: 'var(--wm-text-dim)' }}>
          {unit}
        </span>
      )}
    </div>
  )
}

export { NumberField }
export type { NumberFieldProps }
