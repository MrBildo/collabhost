import type { FieldOption, FieldType } from '@/api/types'
import { BooleanField } from './BooleanField'
import { DirectoryField } from './DirectoryField'
import { KeyValueField } from './KeyValueField'
import { NumberField } from './NumberField'
import { SelectField } from './SelectField'
import { TextField } from './TextField'

type RegistrationFieldProps = {
  fieldKey: string
  label: string
  type: FieldType
  value: unknown
  required: boolean
  placeholder?: string
  helpText?: string
  options?: FieldOption[]
  error?: string
  onChange: (value: unknown) => void
  className?: string
}

function RegistrationField({
  fieldKey,
  label,
  type,
  value,
  required,
  placeholder,
  helpText,
  options,
  error,
  onChange,
  className,
}: RegistrationFieldProps) {
  const fieldId = `reg-${fieldKey}`

  return (
    <div className={className} style={{ marginBottom: '14px' }}>
      <label
        htmlFor={fieldId}
        className="block text-xs font-semibold mb-1"
        style={{
          color: 'var(--wm-text-dim)',
          textTransform: 'uppercase',
          letterSpacing: '0.06em',
          fontSize: '10px',
        }}
      >
        {label}
        {required && <span style={{ color: 'var(--wm-amber)', marginLeft: '2px' }}>*</span>}
      </label>
      {renderField(type, fieldId, value, placeholder, options, !!error, onChange)}
      {helpText && !error && (
        <div className="mt-1" style={{ fontSize: '10px', color: 'var(--wm-text-dim)', fontStyle: 'italic' }}>
          {helpText}
        </div>
      )}
      {error && (
        <div className="mt-1" style={{ fontSize: '10px', color: 'var(--wm-red)' }}>
          {error}
        </div>
      )}
    </div>
  )
}

function renderField(
  type: FieldType,
  fieldId: string,
  value: unknown,
  placeholder: string | undefined,
  options: FieldOption[] | undefined,
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
          placeholder={placeholder}
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
          placeholder={placeholder}
          hasError={hasError}
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

export { RegistrationField }
export type { RegistrationFieldProps }
