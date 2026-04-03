import { cn } from '@/lib/cn'

type BooleanFieldProps = {
  id?: string
  value: boolean
  onChange: (value: boolean) => void
  disabled?: boolean
  className?: string
}

function BooleanField({ id, value, onChange, disabled, className }: BooleanFieldProps) {
  return (
    <button
      id={id}
      type="button"
      role="switch"
      aria-checked={value}
      className={cn('wm-toggle', value && 'wm-toggle--on', className)}
      disabled={disabled}
      onClick={() => onChange(!value)}
    />
  )
}

export { BooleanField }
export type { BooleanFieldProps }
