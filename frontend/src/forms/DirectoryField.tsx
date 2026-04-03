import { ActionButton } from '@/actions/ActionButton'
import { cn } from '@/lib/cn'

type DirectoryFieldProps = {
  id?: string
  value: string
  onChange: (value: string) => void
  disabled?: boolean
  readOnly?: boolean
  hasError?: boolean
  onBrowse?: () => void
  className?: string
}

function DirectoryField({
  id,
  value,
  onChange,
  disabled,
  readOnly,
  hasError,
  onBrowse,
  className,
}: DirectoryFieldProps) {
  return (
    <div className={cn('flex items-center gap-2', className)}>
      <input
        id={id}
        type="text"
        className={cn('wm-input flex-1', hasError && 'wm-input--error', readOnly && 'wm-input--readonly')}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="/path/to/directory"
        disabled={disabled}
        readOnly={readOnly}
      />
      {onBrowse && (
        <ActionButton size="sm" onClick={onBrowse} disabled={disabled}>
          Browse
        </ActionButton>
      )}
    </div>
  )
}

export { DirectoryField }
export type { DirectoryFieldProps }
