import { ActionButton } from '@/actions/ActionButton'
import { ApiError } from '@/api/client'
import type { CreateAppRequest, RegistrationField as RegistrationFieldType, RegistrationSection } from '@/api/types'
import { Breadcrumbs } from '@/chrome/Breadcrumbs'
import { RegistrationField } from '@/forms/RegistrationField'
import { useAppTypes, useCreateApp, useRegistrationSchema } from '@/hooks/use-app-create'
import { useDetectStrategy } from '@/hooks/use-detect-strategy'
import { toSlug } from '@/lib/format'
import { ROUTES } from '@/lib/routes'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { SectionDivider } from '@/shared/SectionDivider'
import { Spinner } from '@/shared/Spinner'
import { TypeCard } from '@/shared/TypeCard'
import { useCallback, useEffect, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'

type FormValues = Record<string, Record<string, unknown>>
type FieldErrors = Record<string, Record<string, string>>

function buildInitialValues(sections: RegistrationSection[]): FormValues {
  const values: FormValues = {}
  for (const section of sections) {
    const sectionValues: Record<string, unknown> = {}
    for (const field of section.fields) {
      sectionValues[field.key] = field.defaultValue
    }
    values[section.key] = sectionValues
  }
  return values
}

function validateForm(sections: RegistrationSection[], values: FormValues): FieldErrors {
  const errors: FieldErrors = {}
  for (const section of sections) {
    for (const field of section.fields) {
      if (field.required) {
        const sectionValues = values[section.key]
        const val = sectionValues?.[field.key]
        if (val == null || val === '' || (typeof val === 'object' && Object.keys(val as object).length === 0)) {
          if (!errors[section.key]) errors[section.key] = {}
          const sectionErrors = errors[section.key]
          if (sectionErrors) sectionErrors[field.key] = 'Required'
        }
      }
    }
  }
  return errors
}

function AppCreatePage() {
  const navigate = useNavigate()
  const appTypesQuery = useAppTypes()
  const createMutation = useCreateApp()

  const [selectedType, setSelectedType] = useState<string | null>(null)
  const [formValues, setFormValues] = useState<FormValues>({})
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [submitError, setSubmitError] = useState<string | null>(null)

  const selectedTypeData = appTypesQuery.data?.find((t) => t.name === selectedType)
  const schemaQuery = useRegistrationSchema(selectedType ?? '')
  const schema = schemaQuery.data

  // Discovery strategy auto-detect: evidence hint shown below the strategy field
  const [strategyHint, setStrategyHint] = useState<string | null>(null)
  // Track whether the user has manually selected a strategy to suppress auto-suggest
  const strategyLockedRef = useRef(false)

  // Track whether the user has manually edited the slug field.
  // Once touched, display name changes no longer auto-derive the slug.
  // Uses a ref to avoid stale closures in handleFieldChange.
  const slugLockedRef = useRef(false)

  // Discovery strategy auto-detect: call detect-strategy when directory changes.
  // Only fires for app types that have a discovery section (process-capable types).
  const artifactLocation = String(formValues.artifact?.location ?? '')
  const hasDiscoverySection = schema?.sections.some((s) => s.key === 'discovery') ?? false
  const detectPath = hasDiscoverySection ? artifactLocation : ''
  const detectQuery = useDetectStrategy(detectPath, selectedType ?? '')

  // Apply auto-detected strategy when detection results arrive
  useEffect(() => {
    if (!detectQuery.data || strategyLockedRef.current) return

    const { suggestedStrategy, evidence } = detectQuery.data
    // Don't auto-fill if the suggestion is manual -- keep whatever default is set
    if (suggestedStrategy === 'manual') return

    setFormValues((prev) => ({
      ...prev,
      discovery: {
        ...prev.discovery,
        discoveryStrategy: suggestedStrategy,
      },
    }))

    if (evidence.length > 0) {
      setStrategyHint(`Detected: ${evidence.join(', ')}`)
    }
  }, [detectQuery.data])

  const handleTypeSelect = useCallback((typeSlug: string) => {
    setSelectedType(typeSlug)
    setFormValues({})
    setFieldErrors({})
    setSubmitError(null)
    setStrategyHint(null)
    slugLockedRef.current = false
    strategyLockedRef.current = false
  }, [])

  // Initialize form values when schema loads for a new type
  const [initializedType, setInitializedType] = useState<string | null>(null)
  if (schema && initializedType !== selectedType) {
    setInitializedType(selectedType)
    setFormValues(buildInitialValues(schema.sections))
  }

  const handleFieldChange = useCallback((sectionKey: string, fieldKey: string, value: unknown) => {
    // If the user directly edits the slug field, lock it so display name
    // changes no longer auto-derive. This stays locked until type change or back.
    if (sectionKey === 'basics' && fieldKey === 'name') {
      slugLockedRef.current = true
    }

    // If the user manually picks a discovery strategy, lock it so detection
    // results no longer override their choice.
    if (sectionKey === 'discovery' && fieldKey === 'discoveryStrategy') {
      strategyLockedRef.current = true
      setStrategyHint(null)
    }

    // If the directory changes, unlock auto-suggest so the next detection
    // can update the strategy field again.
    if (sectionKey === 'artifact' && fieldKey === 'location') {
      strategyLockedRef.current = false
      setStrategyHint(null)
    }

    setFormValues((prev) => {
      const next = {
        ...prev,
        [sectionKey]: {
          ...prev[sectionKey],
          [fieldKey]: value,
        },
      }

      // Auto-derive slug from display name when the slug hasn't been manually edited
      if (sectionKey === 'basics' && fieldKey === 'displayName' && !slugLockedRef.current) {
        next.basics = {
          ...next.basics,
          name: toSlug(String(value ?? '')),
        }
      }

      return next
    })
    setFieldErrors((prev) => {
      const sectionErrors = { ...prev[sectionKey] }
      delete sectionErrors[fieldKey]
      return { ...prev, [sectionKey]: sectionErrors }
    })
  }, [])

  const handleSubmit = useCallback(() => {
    if (!schema || !selectedType) return

    // Client-side validation
    const errors = validateForm(schema.sections, formValues)
    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors)
      return
    }

    // The backend registration schema always emits a "basics" section
    // with well-known field keys: "name" (slug) and "displayName".
    // See AppTypeEndpoints.BuildRegistrationSections for the contract.
    const basicsValues = formValues.basics ?? {}
    const name = String(basicsValues.name ?? '')
    const displayName = String(basicsValues.displayName ?? name)

    const request: CreateAppRequest = {
      name,
      displayName,
      appTypeId: schema.appType.id,
      values: formValues,
    }

    setSubmitError(null)
    createMutation.mutate(request, {
      onSuccess: () => {
        navigate(ROUTES.appDetail(name))
      },
      onError: (error) => {
        if (error instanceof ApiError && error.statusCode === 400) {
          try {
            const parsed = JSON.parse(error.body) as {
              errors?: Array<{ section: string; field: string; message: string }>
            }
            if (parsed.errors) {
              const errs: FieldErrors = {}
              for (const e of parsed.errors) {
                if (!errs[e.section]) errs[e.section] = {}
                const sectionErrors = errs[e.section]
                if (sectionErrors) sectionErrors[e.field] = e.message
              }
              setFieldErrors(errs)
              return
            }
          } catch {
            /* not validation error format */
          }
        }
        setSubmitError(error instanceof Error ? error.message : 'Failed to create app')
      },
    })
  }, [schema, selectedType, formValues, createMutation, navigate])

  const handleBack = useCallback(() => {
    setSelectedType(null)
    setInitializedType(null)
    setFormValues({})
    setFieldErrors({})
    setSubmitError(null)
    setStrategyHint(null)
    slugLockedRef.current = false
    strategyLockedRef.current = false
  }, [])

  // Step state
  const isTypeSelected = selectedType !== null
  const hasSchema = schema !== null && schema !== undefined

  return (
    <div className="flex flex-col" style={{ maxWidth: '720px' }}>
      {/* Breadcrumbs */}
      <Breadcrumbs segments={[{ label: 'Apps', to: ROUTES.apps }, { label: 'Add App' }]} />

      {/* Page title */}
      <h2
        className="mb-5"
        style={{ fontFamily: 'var(--wm-sans)', fontSize: '18px', fontWeight: 700, color: 'var(--wm-text-bright)' }}
      >
        Add App
      </h2>

      {/* Step indicator */}
      <div className="wm-steps">
        <div className={`wm-step ${isTypeSelected ? 'wm-step--done' : 'wm-step--active'}`}>
          <div className="wm-step__num">{isTypeSelected ? '\u2713' : '1'}</div>
          Choose type
        </div>
        <div className={`wm-step__line ${isTypeSelected ? 'wm-step__line--done' : ''}`} />
        <div className={`wm-step ${isTypeSelected && hasSchema ? 'wm-step--active' : ''}`}>
          <div className="wm-step__num">2</div>
          Configure
        </div>
      </div>

      {submitError && <ErrorBanner message={submitError} className="mb-4" onDismiss={() => setSubmitError(null)} />}

      {/* Step 1: Type picker */}
      {!isTypeSelected && (
        <>
          <div
            className="mb-2.5"
            style={{
              fontSize: '10px',
              fontWeight: 600,
              color: 'var(--wm-text-dim)',
              textTransform: 'uppercase',
              letterSpacing: '0.1em',
            }}
          >
            What kind of app?
          </div>

          {appTypesQuery.isLoading && <Spinner />}

          {appTypesQuery.error && (
            <ErrorBanner
              message={appTypesQuery.error instanceof Error ? appTypesQuery.error.message : 'Failed to load app types'}
            />
          )}

          {appTypesQuery.data && (
            <div className="grid grid-cols-3 gap-2 mb-7">
              {appTypesQuery.data.map((appType) => (
                <TypeCard
                  key={appType.id}
                  name={appType.name}
                  displayName={appType.displayName}
                  description={appType.description}
                  tags={appType.tags}
                  isSelected={false}
                  onClick={() => handleTypeSelect(appType.name)}
                />
              ))}
            </div>
          )}
        </>
      )}

      {/* Step 2: Schema-driven form */}
      {isTypeSelected && (
        <>
          {/* Selected type indicator */}
          {selectedTypeData && (
            <div className="flex items-center gap-2 mb-5">
              <div
                className="flex items-center gap-2 px-3 py-1.5"
                style={{
                  background: 'var(--wm-amber-dim)',
                  border: '1px solid var(--wm-amber-border)',
                  borderRadius: 'var(--wm-radius-md)',
                }}
              >
                <span style={{ fontSize: '11px', fontWeight: 600, color: 'var(--wm-amber)' }}>
                  {selectedTypeData.displayName}
                </span>
              </div>
              <ActionButton size="sm" onClick={handleBack}>
                Change
              </ActionButton>
            </div>
          )}

          {schemaQuery.isLoading && <Spinner />}

          {schemaQuery.error && (
            <ErrorBanner
              message={
                schemaQuery.error instanceof Error ? schemaQuery.error.message : 'Failed to load registration schema'
              }
            />
          )}

          {schema && (
            <>
              {schema.sections.map((section) => (
                <div key={section.key} className="mb-6">
                  <SectionDivider label={section.title} className="mb-3" />
                  {section.fields.map((field: RegistrationFieldType) => {
                    const isStrategyField = section.key === 'discovery' && field.key === 'discoveryStrategy'
                    return (
                      <RegistrationField
                        key={field.key}
                        fieldKey={`${section.key}-${field.key}`}
                        label={field.label}
                        type={field.type}
                        value={formValues[section.key]?.[field.key] ?? field.defaultValue}
                        required={field.required}
                        placeholder={field.placeholder}
                        helpText={field.helpText}
                        hint={isStrategyField ? (strategyHint ?? undefined) : undefined}
                        options={field.options}
                        error={fieldErrors[section.key]?.[field.key]}
                        onChange={(val) => handleFieldChange(section.key, field.key, val)}
                      />
                    )
                  })}
                </div>
              ))}

              {/* Form actions */}
              <div className="flex items-center gap-2.5 pt-4 mt-2" style={{ borderTop: '1px solid var(--wm-border)' }}>
                <ActionButton variant="primary" size="lg" onClick={handleSubmit} disabled={createMutation.isPending}>
                  {createMutation.isPending ? 'Creating...' : 'Create App'}
                </ActionButton>
                <ActionButton onClick={() => navigate(ROUTES.apps)} disabled={createMutation.isPending}>
                  Cancel
                </ActionButton>
                <span className="ml-auto" style={{ fontSize: '10px', color: 'var(--wm-text-dim)' }}>
                  Everything else can be configured in Settings after creation.
                </span>
              </div>
            </>
          )}
        </>
      )}
    </div>
  )
}

export { AppCreatePage }
