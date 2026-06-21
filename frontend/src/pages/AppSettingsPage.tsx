import { ActionButton } from '@/actions/ActionButton'
import { ApiError } from '@/api/client'
import type {
  AppSettings,
  RuntimeConfigFileImportResponse,
  SettingsField,
  SettingsSection,
  SettingsValidationError,
} from '@/api/types'
import { Breadcrumbs } from '@/chrome/Breadcrumbs'
import { SchemaField } from '@/forms/SchemaField'
import {
  useAppSettings,
  useDeleteApp,
  useImportRuntimeConfigFile,
  useSaveSettings,
  useSettingsRestartApp,
} from '@/hooks/use-app-settings'
import { useCurrentUser } from '@/hooks/use-current-user'
import { ROUTES } from '@/lib/routes'
import { ConfirmDialog } from '@/shared/ConfirmDialog'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { ImportConfigDialog } from '@/shared/ImportConfigDialog'
import { RestartConfirmDialog } from '@/shared/RestartConfirmDialog'
import { SectionDivider } from '@/shared/SectionDivider'
import { Spinner } from '@/shared/Spinner'
import { useCallback, useMemo, useRef, useState } from 'react'
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

// Card #338 -- dependency resolution. Same-section only (capability boundaries
// are semantic; cross-section deps are explicitly out of contract). The BE
// camelCases enum dependency values at serialization (e.g. "fileServer", not
// "FileServer") to align with Select option values, so the comparator is a
// stringified equality check on the parent's effective value.
function resolveParentValue(
  section: SettingsSection,
  parentKey: string,
  editValues: DirtyFields,
  isEditing: boolean,
): unknown {
  if (isEditing) {
    const edit = editValues[section.key]?.[parentKey]
    if (edit !== undefined) return edit
  }
  return section.fields.find((f) => f.key === parentKey)?.value
}

function isDependencyMet(parentValue: unknown, requiredValue: string): boolean {
  if (parentValue == null) return false
  // Boolean parents: dependency.value is "true" / "false" string-encoded.
  if (typeof parentValue === 'boolean') {
    return String(parentValue) === requiredValue
  }
  // Number / select / text: stringify and compare. Select option values are
  // already camelCased and aligned with the BE-side camelCasing of dep values.
  return String(parentValue) === requiredValue
}

function formatRequiredValueLabel(parentField: SettingsField | undefined, requiredValue: string): string {
  // For Select parents, render the option's display label so the badge reads
  // "Effective only when Serve Mode = File Server" rather than the raw
  // camelCased option value.
  if (parentField?.type === 'select') {
    const match = parentField.options?.find((o) => o.value === requiredValue)
    if (match) return match.label
  }
  // For Boolean parents, "true"/"false" → "On"/"Off" reads naturally in the
  // effectiveness-predicate sentence ("Effective only when Enable HSTS = On").
  if (parentField?.type === 'boolean') {
    if (requiredValue === 'true') return 'On'
    if (requiredValue === 'false') return 'Off'
  }
  return requiredValue
}

function AppSettingsPage() {
  const { slug } = useParams<{ slug: string }>()
  const navigate = useNavigate()

  const settingsQuery = useAppSettings(slug ?? '')
  const saveMutation = useSaveSettings(slug ?? '')
  const restartMutation = useSettingsRestartApp(slug ?? '')
  const deleteMutation = useDeleteApp()
  const importMutation = useImportRuntimeConfigFile(slug ?? '')
  const { data: currentUser } = useCurrentUser()
  const isAdmin = currentUser?.role === 'administrator'

  const [isEditing, setIsEditing] = useState(false)
  const [editValues, setEditValues] = useState<DirtyFields>({})
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({})
  const [isDeleteOpen, setIsDeleteOpen] = useState(false)
  const [deleteError, setDeleteError] = useState<string | null>(null)
  const [isRestartDialogOpen, setIsRestartDialogOpen] = useState(false)
  const [saveError, setSaveError] = useState<string | null>(null)
  // Card #336 — import-preview state. The dialog opens immediately on click
  // and shows a loading body until the preview lands (or an error surfaces).
  const [isImportDialogOpen, setIsImportDialogOpen] = useState(false)
  const [importPreview, setImportPreview] = useState<RuntimeConfigFileImportResponse | null>(null)
  const [importError, setImportError] = useState<string | null>(null)

  // Stash the changes object when the restart dialog opens so both
  // Save Only and Save & Restart use the same payload
  const pendingChangesRef = useRef<Record<string, Record<string, unknown>> | null>(null)

  const settings = settingsQuery.data

  /**
   * Single source of truth for the dirty-fields walk. Returns the changed
   * fields grouped by section, or null when nothing has changed. Both
   * `isDirty` (the save-button enable state) and the save-payload path in
   * `handleSave` derive from this — keeping them on one walk prevents the
   * silent operator-facing defect class where the button says "dirty" but
   * the payload is empty (or vice versa). (#319)
   */
  const computedChanges = useMemo((): Record<string, Record<string, unknown>> | null => {
    if (!settings) return null

    const changes: Record<string, Record<string, unknown>> = {}
    for (const section of settings.sections) {
      for (const field of section.fields) {
        const editVal = editValues[section.key]?.[field.key]
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

    return Object.keys(changes).length > 0 ? changes : null
  }, [settings, editValues])

  const isDirty = computedChanges !== null

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

  /** Check if any of the changed fields have requiresRestart flagged by the API. */
  const hasRestartRequiredChanges = useCallback(
    (changes: Record<string, Record<string, unknown>>): boolean => {
      if (!settings) return false
      for (const section of settings.sections) {
        const sectionChanges = changes[section.key]
        if (!sectionChanges) continue
        for (const field of section.fields) {
          if (field.requiresRestart && sectionChanges[field.key] !== undefined) {
            return true
          }
        }
      }
      return false
    },
    [settings],
  )

  /** Execute the save mutation with shared error handling. */
  const executeSave = useCallback(
    (changes: Record<string, Record<string, unknown>>, andRestart: boolean) => {
      setSaveError(null)
      setFieldErrors({})

      saveMutation.mutate(
        { changes },
        {
          onSuccess: () => {
            setIsEditing(false)
            setEditValues({})
            setIsRestartDialogOpen(false)
            pendingChangesRef.current = null

            if (andRestart && slug) {
              // FE-FORM-03 (#101 class): the save landed but the restart is a
              // separate request that previously failed silently — the operator
              // saw "saved" and believed restart-required settings were live when
              // the app never restarted. Surface the restart failure distinctly
              // from a save failure (the save DID succeed); the page-level banner
              // is the right surface since the dialog has already closed.
              restartMutation.mutate(undefined, {
                onError: (error) => {
                  const reason = error instanceof Error ? error.message : 'unknown error'
                  setSaveError(
                    `Settings saved, but the restart failed: ${reason}. Restart the app to apply the changes.`,
                  )
                },
              })
            }
          },
          onError: (error) => {
            setIsRestartDialogOpen(false)
            pendingChangesRef.current = null

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
    },
    [slug, saveMutation, restartMutation],
  )

  const handleSave = useCallback(() => {
    if (!settings || !slug) return

    if (!computedChanges) {
      setIsEditing(false)
      return
    }

    // If any changed field requires restart, show the confirmation dialog
    if (hasRestartRequiredChanges(computedChanges)) {
      pendingChangesRef.current = computedChanges
      setIsRestartDialogOpen(true)
      return
    }

    // No restart-flagged fields changed — save directly
    executeSave(computedChanges, false)
  }, [settings, slug, computedChanges, hasRestartRequiredChanges, executeSave])

  const handleSaveAndRestart = useCallback(() => {
    const changes = pendingChangesRef.current
    if (!changes) return
    executeSave(changes, true)
  }, [executeSave])

  const handleSaveOnly = useCallback(() => {
    const changes = pendingChangesRef.current
    if (!changes) return
    executeSave(changes, false)
  }, [executeSave])

  const handleRestartDialogCancel = useCallback(() => {
    setIsRestartDialogOpen(false)
    pendingChangesRef.current = null
  }, [])

  const handleDelete = useCallback(() => {
    if (!slug) return
    setDeleteError(null)
    deleteMutation.mutate(slug, {
      onSuccess: () => {
        navigate(ROUTES.apps)
      },
      // FE-FORM-03 (#101 class): a failed delete previously left the dialog open
      // with no signal — the operator clicks "Delete App", nothing visibly
      // happens, the app is still there. Surface the failure inline; the dialog
      // stays open so the message is anchored to the action that produced it.
      onError: (error) => {
        setDeleteError(error instanceof Error ? error.message : 'Failed to delete app')
      },
    })
  }, [slug, deleteMutation, navigate])

  // Card #336 — open the import dialog and kick off the preview call.
  const handleImportClick = useCallback(() => {
    setImportPreview(null)
    setImportError(null)
    setIsImportDialogOpen(true)
    importMutation.mutate(undefined, {
      onSuccess: (response) => {
        setImportPreview(response)
      },
      onError: (error) => {
        if (error instanceof ApiError) {
          // The importer surfaces operator-actionable messages in a Problem
          // Details body — peel the `detail` field when present, otherwise
          // fall back to the raw body / message.
          try {
            const parsed = JSON.parse(error.body) as { detail?: string }
            setImportError(parsed.detail ?? error.message)
          } catch {
            setImportError(error.body || error.message)
          }
        } else {
          setImportError(error instanceof Error ? error.message : 'Failed to read config.json on disk')
        }
      },
    })
  }, [importMutation])

  const handleImportCancel = useCallback(() => {
    setIsImportDialogOpen(false)
    setImportPreview(null)
    setImportError(null)
  }, [])

  // Merge the preview into the runtime-config-file Values edit state. Existing
  // operator-typed entries are preserved; imported keys win on collision. The
  // operator still has to save — this only stages the change in the form.
  const handleImportConfirm = useCallback(() => {
    if (!importPreview) return
    setEditValues((prev) => ({
      ...prev,
      'runtime-config-file': {
        ...prev['runtime-config-file'],
        values: {
          ...((prev['runtime-config-file']?.values as Record<string, string> | undefined) ?? {}),
          ...importPreview.imported,
        },
      },
    }))
    setIsImportDialogOpen(false)
    setImportPreview(null)
    setImportError(null)
  }, [importPreview])

  function getFieldValue(field: SettingsField, sectionKey: string): unknown {
    // While editing, the edit state is the SOLE display source. The previous
    // `?? field.value` fallback was a second source of truth: a field cleared to
    // null/empty would snap the *display* back to the saved value while
    // `computedChanges` armed Save with null — the operator saw the old value but
    // saved a clear (FE-FORM-01, #421). The edit map is seeded for every field by
    // `buildInitialValues` at edit-start, so a present key holding null/empty is a
    // deliberate clear and must render empty (the field components coalesce
    // null → ''). The `field.key in` check keeps display==payload even on the
    // brief pre-seed render.
    if (isEditing && editValues[sectionKey] !== undefined && field.key in editValues[sectionKey]) {
      return editValues[sectionKey][field.key]
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
    <div className="flex flex-col">
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

      {/* Sections */}
      {settings.sections.map((section) => (
        <div key={section.key} className="mb-7">
          <SectionDivider label={section.title} className="mb-3" />
          <div className="flex flex-col gap-0">
            {section.fields.map((field, i) => {
              // Card #338 -- per-render dependency derivation. Same-section only.
              // Resolved here (not inside SchemaField) because the page is the
              // single seam with all sibling field values + edit state in scope.
              let disabledByDependency: { parentLabel: string; requiredValueLabel: string } | undefined
              if (field.dependsOn) {
                const parentValue = resolveParentValue(section, field.dependsOn.field, editValues, isEditing)
                if (!isDependencyMet(parentValue, field.dependsOn.value)) {
                  const parentField = section.fields.find((f) => f.key === field.dependsOn?.field)
                  disabledByDependency = {
                    parentLabel: parentField?.label ?? field.dependsOn.field,
                    requiredValueLabel: formatRequiredValueLabel(parentField, field.dependsOn.value),
                  }
                }
              }

              return (
                <SchemaField
                  key={field.key}
                  fieldKey={`${section.key}-${field.key}`}
                  label={field.label}
                  type={field.type}
                  value={getFieldValue(field, section.key)}
                  defaultValue={field.defaultValue}
                  editable={field.editable}
                  requiresRestart={field.requiresRestart}
                  options={field.options}
                  helpText={field.helpText}
                  unit={field.unit}
                  keyPattern={field.keyPattern}
                  keyPatternMessage={field.keyPatternMessage}
                  disabledByDependency={disabledByDependency}
                  error={fieldErrors[section.key]?.[field.key]}
                  isEditing={isEditing}
                  onChange={(val) => handleFieldChange(section.key, field.key, val)}
                  className={i > 0 ? 'wm-settings-field-separator' : ''}
                />
              )
            })}
            {/* Card #336 — section-specific import affordance. The section is
                otherwise schema-driven; this is the one slot where the page
                knows about a capability by name. */}
            {isEditing && section.key === 'runtime-config-file' && (
              <div className="mt-2 ml-1">
                <ActionButton onClick={handleImportClick} disabled={importMutation.isPending}>
                  Import current config.json
                </ActionButton>
              </div>
            )}
          </div>
        </div>
      ))}

      {/* Danger Zone — only visible in edit mode for admins */}
      {isEditing && isAdmin && (
        <div className="wm-danger-zone mt-10">
          <div className="wm-danger-zone__title">{'// Danger Zone'}</div>
          <p className="mb-3" style={{ fontSize: '14px', color: 'var(--wm-text-dim)', lineHeight: 1.6 }}>
            {
              'Deleting this app will remove it from Collabhost, stop its process, and remove its route. The application files on disk will not be deleted.'
            }
          </p>
          <ActionButton
            variant="danger"
            onClick={() => {
              setDeleteError(null)
              setIsDeleteOpen(true)
            }}
          >
            Delete App
          </ActionButton>
        </div>
      )}

      {/* Delete confirmation dialog */}
      <ConfirmDialog
        title="Delete Application"
        message={`Are you sure you want to delete "${settings.displayName}"? This will stop the process, remove the route, and unregister the app. Application files on disk will not be deleted.`}
        confirmLabel="Delete App"
        confirmVariant="danger"
        isOpen={isDeleteOpen}
        isPending={deleteMutation.isPending}
        error={deleteError}
        onConfirm={handleDelete}
        onCancel={() => {
          setIsDeleteOpen(false)
          setDeleteError(null)
        }}
      />

      {/* Restart confirmation dialog — shown when saving changes to restart-required fields */}
      <RestartConfirmDialog
        isOpen={isRestartDialogOpen}
        isPending={saveMutation.isPending}
        onSaveAndRestart={handleSaveAndRestart}
        onSaveOnly={handleSaveOnly}
        onCancel={handleRestartDialogCancel}
      />

      {/* Card #336 — import preview dialog for runtime-config-file */}
      <ImportConfigDialog
        isOpen={isImportDialogOpen}
        isPending={importMutation.isPending}
        imported={importPreview?.imported ?? null}
        skipped={importPreview?.skipped ?? []}
        sourcePath={importPreview?.sourcePath ?? null}
        error={importError}
        onConfirm={handleImportConfirm}
        onCancel={handleImportCancel}
      />
    </div>
  )
}

export { AppSettingsPage }
