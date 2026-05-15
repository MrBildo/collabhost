import { cn } from '@/lib/cn'
import { useState } from 'react'

// Env-var key contract — the DEFAULT when the schema declares no keyPattern.
// Every existing keyvalue field (environment-defaults.variables) relies on these
// being byte-for-byte unchanged. Do not weaken. (Card #308)
const ENV_KEY_RE = /^[A-Za-z_][A-Za-z0-9_]*$/
const ENV_KEY_MESSAGE =
  'Keys must start with a letter or underscore, and contain only letters, digits, and underscores.'

type KeyValueFieldProps = {
  value: Record<string, string>
  onChange: (value: Record<string, string>) => void
  disabled?: boolean
  className?: string
  // Card #308: schema-driven key validation. Absent => env-var default above.
  keyPattern?: RegExp
  keyPatternMessage?: string
}

// Rebuild a record from positionally-ordered entries so a key rename keeps the
// row in place (no map-reorder, no input remount mid-type). Last-wins on a
// transient duplicate key — acceptable while editing; the server is the gate.
function entriesToRecord(entries: [string, string][]): Record<string, string> {
  const next: Record<string, string> = {}
  for (const [k, v] of entries) {
    next[k] = v
  }
  return next
}

function KeyValueField({
  value,
  onChange,
  disabled,
  className,
  keyPattern = ENV_KEY_RE,
  keyPatternMessage = ENV_KEY_MESSAGE,
}: KeyValueFieldProps) {
  const [newKey, setNewKey] = useState('')
  const [newValue, setNewValue] = useState('')

  const entries = Object.entries(value)

  function isKeyValidValue(key: string): boolean {
    return key === '' || keyPattern.test(key)
  }

  const trimmedKey = newKey.trim()
  const isNewKeyValid = isKeyValidValue(trimmedKey)
  const isDuplicate = trimmedKey !== '' && trimmedKey in value
  const canAdd = trimmedKey !== '' && isNewKeyValid && !isDuplicate

  function handleAdd(): void {
    if (!canAdd) return
    onChange({ ...value, [trimmedKey]: newValue })
    setNewKey('')
    setNewValue('')
  }

  function handleRemove(index: number): void {
    const next = entries.filter((_, i) => i !== index)
    onChange(entriesToRecord(next))
  }

  function handleKeyChange(index: number, key: string): void {
    const next = entries.map(([k, v], i): [string, string] => (i === index ? [key, v] : [k, v]))
    onChange(entriesToRecord(next))
  }

  function handleValueChange(index: number, val: string): void {
    const next = entries.map(([k, v], i): [string, string] => (i === index ? [k, val] : [k, v]))
    onChange(entriesToRecord(next))
  }

  return (
    <div className={cn('wm-kv-table', className)}>
      {entries.map(([k, v], index) => {
        const trimmedRowKey = k.trim()
        const isRowKeyValid = trimmedRowKey === '' || keyPattern.test(trimmedRowKey)
        return (
          // biome-ignore lint/suspicious/noArrayIndexKey: keys are editable inputs; positional identity keeps the input from remounting (losing focus) on rename
          <div key={index} className="wm-kv-row">
            <div className="wm-kv-key" title={k}>
              {disabled ? (
                k
              ) : (
                <input
                  type="text"
                  className="wm-input"
                  style={{
                    border: 'none',
                    background: 'transparent',
                    padding: '0',
                    width: '100%',
                    color: trimmedRowKey && !isRowKeyValid ? 'var(--wm-red)' : 'var(--wm-amber)',
                    fontWeight: 600,
                  }}
                  value={k}
                  onChange={(e) => handleKeyChange(index, e.target.value)}
                  aria-label={`Key for ${k || 'new entry'}`}
                  aria-invalid={trimmedRowKey !== '' && !isRowKeyValid}
                />
              )}
            </div>
            <div className="wm-kv-value">
              <input
                type="text"
                className="wm-input"
                style={{ border: 'none', background: 'transparent', padding: '0', width: '100%' }}
                value={v}
                onChange={(e) => handleValueChange(index, e.target.value)}
                disabled={disabled}
                aria-label={`Value for ${k}`}
              />
            </div>
            <div className="flex items-center justify-center">
              {!disabled && (
                <button
                  type="button"
                  className="wm-kv-remove"
                  onClick={() => handleRemove(index)}
                  aria-label={`Remove ${k}`}
                >
                  x
                </button>
              )}
            </div>
          </div>
        )
      })}
      {!disabled && entries.some(([k]) => k.trim() !== '' && !keyPattern.test(k.trim())) && (
        <div style={{ padding: '4px 12px', fontSize: '12px', color: 'var(--wm-red)' }}>{keyPatternMessage}</div>
      )}
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
                  color: trimmedKey && !isNewKeyValid ? 'var(--wm-red)' : 'var(--wm-amber)',
                }}
                value={newKey}
                onChange={(e) => setNewKey(e.target.value)}
                placeholder="KEY"
                aria-label="New entry key"
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
                aria-label="New entry value"
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
          {trimmedKey && !isNewKeyValid && (
            <div style={{ padding: '4px 12px', fontSize: '12px', color: 'var(--wm-red)' }}>{keyPatternMessage}</div>
          )}
          {isDuplicate && (
            <div style={{ padding: '4px 12px', fontSize: '12px', color: 'var(--wm-red)' }}>Key already exists.</div>
          )}
        </>
      )}
    </div>
  )
}

export { KeyValueField }
export type { KeyValueFieldProps }
