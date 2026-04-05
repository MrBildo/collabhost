import { ActionButton } from '@/actions/ActionButton'
import { cn } from '@/lib/cn'
import { useState } from 'react'
import { DirectoryBrowser } from './DirectoryBrowser'

type DirectoryFieldProps = {
  id?: string
  value: string
  onChange: (value: string) => void
  disabled?: boolean
  readOnly?: boolean
  hasError?: boolean
  className?: string
}

function DirectoryField({ id, value, onChange, disabled, readOnly, hasError, className }: DirectoryFieldProps) {
  const [isBrowsing, setIsBrowsing] = useState(false)

  function handleBrowse(): void {
    setIsBrowsing(true)
  }

  function handleSelect(path: string): void {
    onChange(path)
    setIsBrowsing(false)
  }

  function handleCancel(): void {
    setIsBrowsing(false)
  }

  const isInteractive = !disabled && !readOnly

  return (
    <>
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
        {isInteractive && (
          <ActionButton size="sm" onClick={handleBrowse}>
            Browse
          </ActionButton>
        )}
      </div>
      <DirectoryBrowser isOpen={isBrowsing} initialPath={value} onSelect={handleSelect} onCancel={handleCancel} />
    </>
  )
}

export { DirectoryField }
export type { DirectoryFieldProps }
