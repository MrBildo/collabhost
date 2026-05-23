import type { FieldEditable, FieldOption, FieldType } from '@/api/types'
import { cn } from '@/lib/cn'
import { BooleanField } from './BooleanField'
import { DependencyBadge } from './DependencyBadge'
import { DirectoryField } from './DirectoryField'
import { FormField } from './FormField'
import { KeyValueField } from './KeyValueField'
import { LockBadge } from './LockBadge'
import { NumberField } from './NumberField'
import { OverrideBadge } from './OverrideBadge'
import { RestartBadge } from './RestartBadge'
import { SelectField } from './SelectField'
import { TextField } from './TextField'

type SchemaFieldProps = {
  fieldKey: string
  label: string
  type: FieldType
  value: unknown
  defaultValue: unknown
  editable: FieldEditable
  requiresRestart?: boolean
  options?: FieldOption[]
  helpText?: string
  unit?: string
  // Card #308: server-authoritative key-validation contract for keyvalue fields.
  keyPattern?: string | null
  keyPatternMessage?: string | null
  // Card #338: dependency-unmet rendering. When set, the field stays visible
  // (discoverability) but is disabled and the DependencyBadge announces why.
  // Resolved by AppSettingsPage from the schema-declared DependsOn against the
  // sibling parent's effective value -- not by SchemaField itself, which has no
  // sibling-value scope.
  disabledByDependency?: {
    parentLabel: string
    requiredValueLabel: string
  }
  error?: string
  isEditing: boolean
  onChange: (value: unknown) => void
  className?: string
}

// Compile the server-supplied key-validation regex. Defensive: a malformed
// pattern degrades to "no client-side mirror" (env-var default) rather than
// crashing the settings page — the server remains the authoritative gate. (#308)
function compileKeyPattern(source: string | null | undefined): RegExp | undefined {
  if (typeof source !== 'string' || source === '') return undefined
  try {
    return new RegExp(source)
  } catch {
    return undefined
  }
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
  requiresRestart = false,
  options,
  helpText,
  unit,
  keyPattern,
  keyPatternMessage,
  disabledByDependency,
  error,
  isEditing,
  onChange,
  className,
}: SchemaFieldProps) {
  const isKeyValue = type === 'keyvalue'
  const compiledKeyPattern = compileKeyPattern(keyPattern)
  // The pattern and its message are a pair. If no pattern is in effect (absent,
  // null, or a malformed source that failed to compile), the message must also
  // fall back to the env-var default — never ship a custom message with the
  // env-var regex, or the operator sees a mismatched explanation. (#308)
  const keyPatternMessageValue = compiledKeyPattern ? (keyPatternMessage ?? undefined) : undefined
  const isLocked = editable.mode === 'locked'
  const isDerived = editable.mode === 'derived'
  const isReadOnly = isLocked || isDerived
  const isDependencyUnmet = !!disabledByDependency
  // keyvalue fields are inherently customized per-app — override badge is noise.
  // Card #338: a non-default value that is currently inert is also noise; suppress
  // the Override badge while dependency-unmet.
  const hasOverride = !isKeyValue && !isDependencyUnmet && isOverridden(value, defaultValue)
  const fieldId = `field-${fieldKey}`
  // Restart badge only shows in edit mode for editable fields whose value can
  // actually take effect. Card #338 -- suppress while dependency-unmet (a value
  // that won't take effect can't trigger a restart either).
  const showRestartBadge = requiresRestart && isEditing && !isReadOnly && !isDependencyUnmet

  const badges = (
    <>
      {isLocked && <LockBadge reason={editable.mode === 'locked' ? editable.reason : ''} />}
      {isDerived && <LockBadge reason={editable.mode === 'derived' ? editable.reason : ''} />}
      {hasOverride && !isReadOnly && <OverrideBadge />}
      {showRestartBadge && <RestartBadge />}
      {isDependencyUnmet && (
        <DependencyBadge
          parentLabel={disabledByDependency.parentLabel}
          requiredValueLabel={disabledByDependency.requiredValueLabel}
        />
      )}
    </>
  )

  // Read mode or locked/derived fields -- show plain text
  if (!isEditing || isReadOnly) {
    // keyvalue type gets its own read rendering
    if (isKeyValue) {
      const kvValue = (value as Record<string, string>) ?? {}
      return (
        <FormField label={label} helpText={helpText} error={error} badges={badges} className={className}>
          <KeyValueField
            value={kvValue}
            onChange={onChange}
            disabled
            keyPattern={compiledKeyPattern}
            keyPatternMessage={keyPatternMessageValue}
          />
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
      {renderEditField(
        type,
        fieldId,
        value,
        options,
        unit,
        !!error,
        onChange,
        compiledKeyPattern,
        keyPatternMessageValue,
        isDependencyUnmet,
      )}
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
  keyPattern: RegExp | undefined,
  keyPatternMessage: string | undefined,
  disabled: boolean,
): React.ReactNode {
  switch (type) {
    case 'text':
      return (
        <TextField
          id={fieldId}
          value={String(value ?? '')}
          onChange={onChange}
          hasError={hasError}
          disabled={disabled}
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
          disabled={disabled}
          unit={unit}
          className="max-w-[120px]"
        />
      )

    case 'boolean':
      return <BooleanField id={fieldId} value={!!value} onChange={onChange} disabled={disabled} />

    case 'select':
      return (
        <SelectField
          id={fieldId}
          value={String(value ?? '')}
          onChange={onChange}
          options={options ?? []}
          hasError={hasError}
          disabled={disabled}
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
          disabled={disabled}
          className="max-w-md"
        />
      )

    case 'keyvalue':
      return (
        <KeyValueField
          value={(value as Record<string, string>) ?? {}}
          onChange={onChange}
          disabled={disabled}
          keyPattern={keyPattern}
          keyPatternMessage={keyPatternMessage}
        />
      )
  }
}

export { SchemaField }
export type { SchemaFieldProps }
