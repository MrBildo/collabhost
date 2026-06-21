import { cn } from '@/lib/cn'
import { useRef, useState } from 'react'

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

// Rebuild a record from positionally-ordered rows so a key rename keeps the row
// in place (no map-reorder, no input remount mid-type). A transient duplicate
// key collapses here (last-wins) — but it ONLY collapses in the record handed to
// the parent; the component's own positional `rows` state keeps both rows alive,
// so the operator never loses the sibling row they typed. (Card #421 / FE-FORM-02)
function entriesToRecord(rows: [string, string][]): Record<string, string> {
  const next: Record<string, string> = {}
  for (const [k, v] of rows) {
    next[k] = v
  }
  return next
}

// Shallow record equality — same keys, same values. Used to tell an externally
// driven `value` change (parent reset, Import-merge) apart from the controlled
// round-trip of a record this component just emitted.
function recordsEqual(a: Record<string, string>, b: Record<string, string>): boolean {
  const aKeys = Object.keys(a)
  if (aKeys.length !== Object.keys(b).length) return false
  for (const k of aKeys) {
    if (!(k in b) || a[k] !== b[k]) return false
  }
  return true
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

  // Positional edit state is the authoritative display source while editing.
  // A `Record` can't represent a transient duplicate key (two rows mid-rename
  // would collapse to one and silently destroy the sibling row — FE-FORM-02), so
  // the rows live here and only collapse to a record at the parent-onChange seam.
  const [rows, setRows] = useState<[string, string][]>(() => Object.entries(value))

  // The last record THIS component emitted (or initially adopted). When the
  // incoming `value` differs from it, the parent changed `value` out from under
  // us (reset / Import-merge) and we adopt the new value; otherwise our `rows`
  // are authoritative — they may hold a transient duplicate the record dropped.
  const emittedRef = useRef<Record<string, string>>(value)
  if (!recordsEqual(value, emittedRef.current)) {
    emittedRef.current = value
    setRows(Object.entries(value))
  }

  function emit(nextRows: [string, string][]): void {
    setRows(nextRows)
    const record = entriesToRecord(nextRows)
    emittedRef.current = record
    onChange(record)
  }

  function isKeyValidValue(key: string): boolean {
    return key === '' || keyPattern.test(key)
  }

  const trimmedKey = newKey.trim()
  const isNewKeyValid = isKeyValidValue(trimmedKey)
  const isDuplicate = trimmedKey !== '' && rows.some(([k]) => k === trimmedKey)
  const canAdd = trimmedKey !== '' && isNewKeyValid && !isDuplicate

  // Non-empty keys that appear on more than one row. A rename can transiently
  // produce one (the record collapses dupes on save, last-wins); flagging it
  // tells the operator which rows will merge instead of letting it happen silently.
  const keyCounts = new Map<string, number>()
  for (const [k] of rows) {
    const trimmed = k.trim()
    if (trimmed !== '') keyCounts.set(trimmed, (keyCounts.get(trimmed) ?? 0) + 1)
  }
  const hasDuplicateRowKey = Array.from(keyCounts.values()).some((count) => count > 1)

  function handleAdd(): void {
    if (!canAdd) return
    emit([...rows, [trimmedKey, newValue]])
    setNewKey('')
    setNewValue('')
  }

  function handleRemove(index: number): void {
    emit(rows.filter((_, i) => i !== index))
  }

  function handleKeyChange(index: number, key: string): void {
    emit(rows.map(([k, v], i): [string, string] => (i === index ? [key, v] : [k, v])))
  }

  function handleValueChange(index: number, val: string): void {
    emit(rows.map(([k, v], i): [string, string] => (i === index ? [k, val] : [k, v])))
  }

  return (
    <div className={cn('wm-kv-table', className)}>
      {rows.map(([k, v], index) => {
        const trimmedRowKey = k.trim()
        const isRowKeyValid = trimmedRowKey === '' || keyPattern.test(trimmedRowKey)
        const isRowKeyDuplicate = trimmedRowKey !== '' && (keyCounts.get(trimmedRowKey) ?? 0) > 1
        const isRowKeyFlagged = (trimmedRowKey !== '' && !isRowKeyValid) || isRowKeyDuplicate
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
                    color: isRowKeyFlagged ? 'var(--wm-red)' : 'var(--wm-amber)',
                    fontWeight: 600,
                  }}
                  value={k}
                  onChange={(e) => handleKeyChange(index, e.target.value)}
                  aria-label={`Key for ${k || 'new entry'}`}
                  aria-invalid={isRowKeyFlagged}
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
      {!disabled && rows.some(([k]) => k.trim() !== '' && !keyPattern.test(k.trim())) && (
        <div style={{ padding: '4px 12px', fontSize: '12px', color: 'var(--wm-red)' }}>{keyPatternMessage}</div>
      )}
      {!disabled && hasDuplicateRowKey && (
        <div style={{ padding: '4px 12px', fontSize: '12px', color: 'var(--wm-red)' }}>
          Two rows share the same key — only the last will be saved. Rename one to keep both.
        </div>
      )}
      {!disabled && (
        <>
          <div
            className="wm-kv-row"
            style={{ borderTop: rows.length > 0 ? '1px solid var(--wm-border-subtle)' : 'none' }}
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
