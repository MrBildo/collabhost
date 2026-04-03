import { ActionButton } from '@/actions/ActionButton'
import { ApiError } from '@/api/client'
import type { AppSettings, SettingsField, SettingsValidationError } from '@/api/types'
import { Breadcrumbs } from '@/chrome/Breadcrumbs'
import { SchemaField } from '@/forms/SchemaField'
import { useAppSettings, useDeleteApp, useSaveSettings } from '@/hooks/use-app-settings'
import { ROUTES } from '@/lib/routes'
import { ConfirmDialog } from '@/shared/ConfirmDialog'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { SectionDivider } from '@/shared/SectionDivider'
import { Spinner } from '@/shared/Spinner'
import { useCallback, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'

type DirtyFields = Record<string, Record<string, unknown>>
type FieldErrors = Record<string, Record<string, string>>

function buildInitialValues(settings: AppSettings): DirtyFields {
  const values: DirtyFields = {}
  for (const section of settings.sections) {
    const sectionValues: Record<string, unknown> = {}
    for (const field of section.fields) {
      sectionValues[field.key] = field.value
    }
    values[section.key] = sectionValues
  }
  return values
}

function AppSettingsPage() {
  const { slug } = useParams<{ slug: string }>()
  const navigate = useNavigate()

  const settingsQuery = useAppSettings(slug ?? '')
  const saveMutation = useSaveSettings(slug ?? '')
  const deleteMutation = useDeleteApp()

  const [isEditing, setIsEditing] = useState(false)
  const [editValues, setEditValues] = useState<DirtyFields>({})
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [isDeleteOpen, setIsDeleteOpen] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)

  const settings = settingsQuery.data

  const isDirty = useMemo(() => {
    if (!settings) return false
    for (const section of settings.sections) {
      for (const field of section.fields) {
        const editVal = editValues[section.key]?.[field.key]
        if (editVal !== undefined && editVal !== field.value) {
          if (typeof editVal === 'object' && typeof field.value === 'object') {
            if (JSON.stringify(editVal) !== JSON.stringify(field.value)) return true
          } else {
            return true
          }
        }
      }
    }
    return false
  }, [settings, editValues])

  const handleEdit = useCallback(() => {
    if (!settings) return
    setEditValues(buildInitialValues(settings))
    setFieldErrors({})
    setSaveError(null)
    setIsEditing(true)
  }, [settings])

  const handleCancel = useCallback(() => {
    setIsEditing(false)
    setEditValues({})
    setFieldErrors({})
    setSaveError(null)
  }, [])

  const handleFieldChange = useCallback((sectionKey: string, fieldKey: string, value: unknown) => {
    setEditValues((prev) => ({
      ...prev,
      [sectionKey]: {
        ...prev[sectionKey],
        [fieldKey]: value,
      },
    }))
    // Clear field error on change
    setFieldErrors((prev) => {
      const sectionErrors = { ...prev[sectionKey] }
      delete sectionErrors[fieldKey]
      return { ...prev, [sectionKey]: sectionErrors }
    })
  }, [])

  const handleSave = useCallback(() => {
    if (!settings || !slug) return

    // Build changes object: only include fields that changed
    const changes: Record<string, Record<string, unknown>> = {}
    for (const section of settings.sections) {
      for (const field of section.fields) {
        const sectionEditValues = editValues[section.key]
        const editVal = sectionEditValues?.[field.key]
        if (editVal !== undefined && editVal !== field.value) {
          const isObjectChange = typeof editVal === 'object' && typeof field.value === 'object'
          const hasChanged = isObjectChange ? JSON.stringify(editVal) !== JSON.stringify(field.value) : true
          if (hasChanged) {
            if (!changes[section.key]) changes[section.key] = {}
            const sectionChanges = changes[section.key]
            if (sectionChanges) sectionChanges[field.key] = editVal
          }
        }
      }
    }

    if (Object.keys(changes).length === 0) {
      setIsEditing(false)
      return
    }

    setSaveError(null)
    setFieldErrors({})

    saveMutation.mutate(
      { changes },
      {
        onSuccess: () => {
          setIsEditing(false)
          setEditValues({})
        },
        onError: (error) => {
          if (error instanceof ApiError && error.statusCode === 400) {
            try {
              const parsed = JSON.parse(error.body) as SettingsValidationError
              const errors: FieldErrors = {}
              for (const e of parsed.errors) {
                if (!errors[e.section]) errors[e.section] = {}
                const sectionErrors = errors[e.section]
                if (sectionErrors) sectionErrors[e.field] = e.message
              }
              setFieldErrors(errors)
            } catch {
              setSaveError(error.message)
            }
          } else {
            setSaveError(error instanceof Error ? error.message : 'Failed to save settings')
          }
        },
      },
    )
  }, [settings, slug, editValues, saveMutation])

  const handleDelete = useCallback(() => {
    if (!slug) return
    deleteMutation.mutate(slug, {
      onSuccess: () => {
        navigate(ROUTES.apps)
      },
    })
  }, [slug, deleteMutation, navigate])

  function getFieldValue(field: SettingsField, sectionKey: string): unknown {
    if (isEditing && editValues[sectionKey] !== undefined) {
      return editValues[sectionKey][field.key] ?? field.value
    }
    return field.value
  }

  if (!slug) {
    return <ErrorBanner message="No app slug provided" />
  }

  if (settingsQuery.isLoading) {
    return (
      <div className="py-8">
        <Spinner />
      </div>
    )
  }

  if (settingsQuery.error) {
    return (
      <ErrorBanner
        message={settingsQuery.error instanceof Error ? settingsQuery.error.message : 'Failed to load settings'}
      />
    )
  }

  if (!settings) {
    return <ErrorBanner message="Settings not found" />
  }

  return (
    <div className="flex flex-col" style={{ maxWidth: '720px' }}>
      {/* Breadcrumbs with edit/save actions */}
      <Breadcrumbs
        segments={[
          { label: 'Apps', to: ROUTES.apps },
          { label: settings.displayName, to: ROUTES.appDetail(slug) },
          { label: 'Settings' },
        ]}
        actions={
          isEditing ? (
            <div className="flex items-center gap-2">
              <ActionButton onClick={handleCancel} disabled={saveMutation.isPending}>
                Cancel
              </ActionButton>
              <ActionButton variant="primary" onClick={handleSave} disabled={!isDirty || saveMutation.isPending}>
                {saveMutation.isPending ? 'Saving...' : 'Save Changes'}
              </ActionButton>
            </div>
          ) : (
            <ActionButton variant="amber" onClick={handleEdit}>
              Edit
            </ActionButton>
          )
        }
      />

      {/* Save error */}
      {saveError && <ErrorBanner message={saveError} className="mb-4" onDismiss={() => setSaveError(null)} />}

      {/* Settings title */}
      <h2
        className="mb-1"
        style={{ fontFamily: 'var(--wm-sans)', fontSize: '18px', fontWeight: 700, color: 'var(--wm-text-bright)' }}
      >
        {settings.displayName} Settings
      </h2>
      <p className="mb-6" style={{ fontSize: '11px', color: 'var(--wm-text-dim)' }}>
        {`Configure this app's identity, process behavior, routing, and environment.`}
      </p>

      {/* Sections */}
      {settings.sections.map((section) => (
        <div key={section.key} className="mb-7">
          <SectionDivider label={section.title} className="mb-3" />
          <div className="flex flex-col gap-0">
            {section.fields.map((field, i) => (
              <SchemaField
                key={field.key}
                fieldKey={`${section.key}-${field.key}`}
                label={field.label}
                type={field.type}
                value={getFieldValue(field, section.key)}
                defaultValue={field.defaultValue}
                editable={field.editable}
                options={field.options}
                helpText={field.helpText}
                unit={field.unit}
                error={fieldErrors[section.key]?.[field.key]}
                isEditing={isEditing}
                onChange={(val) => handleFieldChange(section.key, field.key, val)}
                className={i > 0 ? 'wm-settings-field-separator' : ''}
              />
            ))}
          </div>
        </div>
      ))}

      {/* Danger Zone */}
      <div className="wm-danger-zone mt-10">
        <div className="wm-danger-zone__title">{'// Danger Zone'}</div>
        <p className="mb-3" style={{ fontSize: '11px', color: 'var(--wm-text-dim)', lineHeight: 1.6 }}>
          {
            'Deleting this app will remove it from Collabhost, stop its process, and remove its Caddy route. The application files on disk will not be deleted.'
          }
        </p>
        <ActionButton variant="danger" onClick={() => setIsDeleteOpen(true)}>
          Delete App
        </ActionButton>
      </div>

      {/* Delete confirmation dialog */}
      <ConfirmDialog
        title="Delete Application"
        message={`Are you sure you want to delete "${settings.displayName}"? This will stop the process, remove the Caddy route, and unregister the app. Application files on disk will not be deleted.`}
        confirmLabel="Delete App"
        confirmVariant="danger"
        isOpen={isDeleteOpen}
        isPending={deleteMutation.isPending}
        onConfirm={handleDelete}
        onCancel={() => setIsDeleteOpen(false)}
      />
    </div>
  )
}

export { AppSettingsPage }
