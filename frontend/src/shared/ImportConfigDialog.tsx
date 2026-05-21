import { ActionButton } from '@/actions/ActionButton'
import { useEffect, useRef } from 'react'

type ImportConfigDialogProps = {
  isOpen: boolean
  isPending: boolean
  // Result of the preview call. When `null`, the dialog renders nothing
  // beyond a loading body — the parent should keep `isPending` truthy until
  // either the preview lands or the error surfaces.
  imported: Record<string, string> | null
  skipped: string[]
  sourcePath: string | null
  // Wire-level failure (file missing, parse error, etc.) — the message
  // comes from the importer endpoint's 400 response.
  error: string | null
  onConfirm: () => void
  onCancel: () => void
}

// Card #336. Confirmation modal for the runtime-config-file importer. The
// importer endpoint is a preview — it does not persist. This dialog surfaces
// the imported keys and any skipped non-flat entries so the operator can
// decide whether to merge the preview into the form's editable values. On
// confirm, the parent (`AppSettingsPage`) writes the imported map into the
// runtime-config-file section's `editValues`; the save happens via the
// standard settings-save path.
function ImportConfigDialog({
  isOpen,
  isPending,
  imported,
  skipped,
  sourcePath,
  error,
  onConfirm,
  onCancel,
}: ImportConfigDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = dialogRef.current
    if (!dialog) return

    if (isOpen && !dialog.open) {
      dialog.showModal()
    } else if (!isOpen && dialog.open) {
      dialog.close()
    }
  }, [isOpen])

  function handleCancel(e: React.SyntheticEvent): void {
    // Native cancel event fires on Escape — prevent default close
    // so we route through onCancel for consistent behavior.
    e.preventDefault()
    onCancel()
  }

  function handleClick(e: React.MouseEvent<HTMLDialogElement>): void {
    // Backdrop click — dialog element itself, not its children.
    if (e.target === dialogRef.current) {
      onCancel()
    }
  }

  const importedEntries = imported ? Object.entries(imported) : []
  const hasPreview = imported !== null && error === null
  const hasImported = importedEntries.length > 0

  return (
    // biome-ignore lint/a11y/useKeyWithClickEvents: native dialog handles Escape via onCancel; onClick is for backdrop dismiss only
    <dialog
      ref={dialogRef}
      className="wm-dialog-overlay"
      aria-labelledby="import-config-dialog-title"
      onCancel={handleCancel}
      onClick={handleClick}
    >
      <div className="wm-dialog">
        <div className="wm-dialog__title" id="import-config-dialog-title">
          Import current config.json
        </div>
        <div className="wm-dialog__body">
          {error !== null && <div style={{ color: 'var(--wm-red)', marginBottom: '8px' }}>{error}</div>}
          {hasPreview && (
            <>
              <p style={{ marginBottom: '8px' }}>
                {hasImported
                  ? 'The following entries will pre-populate the Values editor. Review them, then confirm to merge into the form. Nothing is saved until you save the settings.'
                  : 'No flat string entries were found in the file.'}
              </p>
              {sourcePath && (
                <p style={{ fontSize: '12px', color: 'var(--wm-text-dim)', marginBottom: '8px' }}>
                  Source: <code>{sourcePath}</code>
                </p>
              )}
              {hasImported && (
                <div
                  style={{
                    border: '1px solid var(--wm-border-subtle)',
                    padding: '6px 10px',
                    marginBottom: '8px',
                    maxHeight: '180px',
                    overflowY: 'auto',
                  }}
                >
                  {importedEntries.map(([k, v]) => (
                    <div
                      key={k}
                      style={{ display: 'flex', gap: '12px', fontFamily: 'var(--wm-mono)', fontSize: '12px' }}
                    >
                      <span style={{ color: 'var(--wm-amber)', fontWeight: 600 }}>{k}</span>
                      <span style={{ color: 'var(--wm-text-dim)' }}>{v}</span>
                    </div>
                  ))}
                </div>
              )}
              {skipped.length > 0 && (
                <div style={{ color: 'var(--wm-red)', fontSize: '12px' }}>
                  <div style={{ marginBottom: '4px' }}>
                    Skipped {skipped.length} non-flat entr{skipped.length === 1 ? 'y' : 'ies'} (only flat
                    string-to-string entries are imported — nested objects, arrays, nulls, and non-string primitives are
                    ignored):
                  </div>
                  <ul style={{ margin: 0, paddingLeft: '18px' }}>
                    {skipped.map((k) => (
                      <li key={k}>
                        <code>{k}</code>
                      </li>
                    ))}
                  </ul>
                </div>
              )}
            </>
          )}
          {!hasPreview && error === null && <div style={{ color: 'var(--wm-text-dim)' }}>Loading preview...</div>}
        </div>
        <div className="wm-dialog__actions">
          <ActionButton onClick={onCancel} disabled={isPending}>
            Cancel
          </ActionButton>
          <ActionButton variant="primary" onClick={onConfirm} disabled={isPending || !hasPreview || !hasImported}>
            {isPending ? 'Importing...' : 'Apply'}
          </ActionButton>
        </div>
      </div>
    </dialog>
  )
}

export { ImportConfigDialog }
export type { ImportConfigDialogProps }
