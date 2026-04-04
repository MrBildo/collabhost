import type { FieldEditable, FieldOption, FieldType } from '@/api/types'
import { cn } from '@/lib/cn'
import { BooleanField } from './BooleanField'
import { DirectoryField } from './DirectoryField'
import { FormField } from './FormField'
import { KeyValueField } from './KeyValueField'
import { LockBadge } from './LockBadge'
import { NumberField } from './NumberField'
import { OverrideBadge } from './OverrideBadge'
import { SelectField } from './SelectField'
import { TextField } from './TextField'

type SchemaFieldProps = {
  fieldKey: string
  label: string
  type: FieldType
  value: unknown
  defaultValue: unknown
  editable: FieldEditable
  options?: FieldOption[]
  helpText?: string
  unit?: string
  error?: string
  isEditing: boolean
  onChange: (value: unknown) => void
  className?: string
}

function isOverridden(value: unknown, defaultValue: unknown): boolean {
  if (value === defaultValue) return false
  if (value == null && defaultValue == null) return false
  if (typeof value === 'object' && typeof defaultValue === 'object') {
    return JSON.stringify(value) !== JSON.stringify(defaultValue)
  }
  return true
}

function formatReadValue(type: FieldType, value: unknown, options?: FieldOption[]): string {
  if (value == null) return '--'
  if (type === 'boolean') return value ? 'Yes' : 'No'
  if (type === 'select' && options) {
    const str = String(value)
    const match = options.find((o) => o.value.toLowerCase() === str.toLowerCase())
    return match ? match.label : String(value)
  }
  return String(value)
}

function SchemaField({
  fieldKey,
  label,
  type,
  value,
  defaultValue,
  editable,
  options,
  helpText,
  unit,
  error,
  isEditing,
  onChange,
  className,
}: SchemaFieldProps) {
  const isLocked = editable.mode === 'locked'
  const isDerived = editable.mode === 'derived'
  const isReadOnly = isLocked || isDerived
  const hasOverride = isOverridden(value, defaultValue)
  const fieldId = `field-${fieldKey}`

  const badges = (
    <>
      {isLocked && <LockBadge reason={editable.mode === 'locked' ? editable.reason : ''} />}
      {isDerived && <LockBadge reason={editable.mode === 'derived' ? editable.reason : ''} />}
      {hasOverride && !isReadOnly && <OverrideBadge />}
    </>
  )

  const isKeyValue = type === 'keyValue' || type === 'keyvalue'

  // Read mode or locked/derived fields -- show plain text
  if (!isEditing || isReadOnly) {
    // keyValue type gets its own read rendering
    if (isKeyValue) {
      const kvValue = (value as Record<string, string>) ?? {}
      return (
        <FormField label={label} helpText={helpText} error={error} badges={badges} className={className}>
          <KeyValueField value={kvValue} onChange={onChange} disabled />
        </FormField>
      )
    }

    return (
      <FormField label={label} helpText={helpText} error={error} badges={badges} className={className}>
        <div className={cn('wm-field-value', (isLocked || isDerived) && 'wm-field-value--dim')}>
          {formatReadValue(type, value, options)}
          {unit && (
            <span
              className="text-xs"
              style={{ color: 'var(--wm-text-dim)', textTransform: 'uppercase', letterSpacing: '0.04em' }}
            >
              {unit}
            </span>
          )}
        </div>
      </FormField>
    )
  }

  // Edit mode -- render the appropriate field component
  return (
    <FormField label={label} htmlFor={fieldId} helpText={helpText} error={error} badges={badges} className={className}>
      {renderEditField(type, fieldId, value, options, unit, !!error, onChange)}
    </FormField>
  )
}

function renderEditField(
  type: FieldType,
  fieldId: string,
  value: unknown,
  options: FieldOption[] | undefined,
  unit: string | undefined,
  hasError: boolean,
  onChange: (value: unknown) => void,
): React.ReactNode {
  switch (type) {
    case 'text':
      return (
        <TextField
          id={fieldId}
          value={String(value ?? '')}
          onChange={onChange}
          hasError={hasError}
          className="max-w-sm"
        />
      )

    case 'number':
      return (
        <NumberField
          id={fieldId}
          value={value as number | null}
          onChange={onChange}
          hasError={hasError}
          unit={unit}
          className="max-w-[120px]"
        />
      )

    case 'boolean':
      return <BooleanField id={fieldId} value={!!value} onChange={onChange} />

    case 'select':
      return (
        <SelectField
          id={fieldId}
          value={String(value ?? '')}
          onChange={onChange}
          options={options ?? []}
          hasError={hasError}
          className="max-w-[200px]"
        />
      )

    case 'directory':
      return (
        <DirectoryField
          id={fieldId}
          value={String(value ?? '')}
          onChange={onChange}
          hasError={hasError}
          className="max-w-md"
        />
      )

    case 'keyValue':
    case 'keyvalue':
      return <KeyValueField value={(value as Record<string, string>) ?? {}} onChange={onChange} />
  }
}

export { SchemaField }
export type { SchemaFieldProps }
