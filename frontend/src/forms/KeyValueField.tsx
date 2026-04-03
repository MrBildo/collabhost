import { cn } from '@/lib/cn'
import { useState } from 'react'

type KeyValueFieldProps = {
  value: Record<string, string>
  onChange: (value: Record<string, string>) => void
  disabled?: boolean
  className?: string
}

function KeyValueField({ value, onChange, disabled, className }: KeyValueFieldProps) {
  const [newKey, setNewKey] = useState('')
  const [newValue, setNewValue] = useState('')

  const entries = Object.entries(value)

  function handleAdd(): void {
    if (!newKey.trim()) return
    onChange({ ...value, [newKey.trim()]: newValue })
    setNewKey('')
    setNewValue('')
  }

  function handleRemove(key: string): void {
    const next = { ...value }
    delete next[key]
    onChange(next)
  }

  function handleValueChange(key: string, val: string): void {
    onChange({ ...value, [key]: val })
  }

  return (
    <div className={cn('wm-kv-table', className)}>
      {entries.map(([k, v]) => (
        <div key={k} className="wm-kv-row">
          <div className="wm-kv-key">{k}</div>
          <div className="wm-kv-value">
            <input
              type="text"
              className="wm-input"
              style={{ border: 'none', background: 'transparent', padding: '0', width: '100%' }}
              value={v}
              onChange={(e) => handleValueChange(k, e.target.value)}
              disabled={disabled}
            />
          </div>
          <div className="flex items-center justify-center">
            {!disabled && (
              <button type="button" className="wm-kv-remove" onClick={() => handleRemove(k)} aria-label={`Remove ${k}`}>
                x
              </button>
            )}
          </div>
        </div>
      ))}
      {!disabled && (
        <div
          className="wm-kv-row"
          style={{ borderTop: entries.length > 0 ? '1px solid var(--wm-border-subtle)' : 'none' }}
        >
          <div className="wm-kv-key" style={{ color: 'var(--wm-text-dim)' }}>
            <input
              type="text"
              className="wm-input"
              style={{
                border: 'none',
                background: 'transparent',
                padding: '0',
                width: '100%',
                color: 'var(--wm-amber)',
              }}
              value={newKey}
              onChange={(e) => setNewKey(e.target.value)}
              placeholder="KEY"
              onKeyDown={(e) => {
                if (e.key === 'Enter') handleAdd()
              }}
            />
          </div>
          <div className="wm-kv-value">
            <input
              type="text"
              className="wm-input"
              style={{ border: 'none', background: 'transparent', padding: '0', width: '100%' }}
              value={newValue}
              onChange={(e) => setNewValue(e.target.value)}
              placeholder="value"
              onKeyDown={(e) => {
                if (e.key === 'Enter') handleAdd()
              }}
            />
          </div>
          <div className="flex items-center justify-center">
            <button
              type="button"
              className="text-xs"
              style={{ color: 'var(--wm-green)', cursor: 'pointer' }}
              onClick={handleAdd}
              aria-label="Add entry"
            >
              +
            </button>
          </div>
        </div>
      )}
    </div>
  )
}

export { KeyValueField }
export type { KeyValueFieldProps }
