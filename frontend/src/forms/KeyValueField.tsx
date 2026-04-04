import { cn } from '@/lib/cn'
import { useState } from 'react'

const ENV_KEY_RE = /^[A-Za-z_][A-Za-z0-9_]*$/

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
  const trimmedKey = newKey.trim()
  const isKeyValid = trimmedKey === '' || ENV_KEY_RE.test(trimmedKey)
  const isDuplicate = trimmedKey !== '' && trimmedKey in value
  const canAdd = trimmedKey !== '' && isKeyValid && !isDuplicate

  function handleAdd(): void {
    if (!canAdd) return
    onChange({ ...value, [trimmedKey]: newValue })
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
        <>
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
                  color: trimmedKey && !isKeyValid ? 'var(--wm-red)' : 'var(--wm-amber)',
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
                style={{
                  color: canAdd ? 'var(--wm-green)' : 'var(--wm-text-dim)',
                  cursor: canAdd ? 'pointer' : 'default',
                }}
                onClick={handleAdd}
                disabled={!canAdd}
                aria-label="Add entry"
              >
                +
              </button>
            </div>
          </div>
          {trimmedKey && !isKeyValid && (
            <div style={{ padding: '4px 12px', fontSize: '10px', color: 'var(--wm-red)' }}>
              Keys must start with a letter or underscore, and contain only letters, digits, and underscores.
            </div>
          )}
          {isDuplicate && (
            <div style={{ padding: '4px 12px', fontSize: '10px', color: 'var(--wm-red)' }}>Key already exists.</div>
          )}
        </>
      )}
    </div>
  )
}

export { KeyValueField }
export type { KeyValueFieldProps }
