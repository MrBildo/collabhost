import { ActionButton } from '@/actions/ActionButton'
import { useEffect, useRef } from 'react'

type RestartConfirmDialogProps = {
  isOpen: boolean
  isPending: boolean
  onSaveAndRestart: () => void
  onSaveOnly: () => void
  onCancel: () => void
}

function RestartConfirmDialog({
  isOpen,
  isPending,
  onSaveAndRestart,
  onSaveOnly,
  onCancel,
}: RestartConfirmDialogProps) {
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
    e.preventDefault()
    onCancel()
  }

  function handleClick(e: React.MouseEvent<HTMLDialogElement>): void {
    if (e.target === dialogRef.current) {
      onCancel()
    }
  }

  return (
    // biome-ignore lint/a11y/useKeyWithClickEvents: native dialog handles Escape via onCancel; onClick is for backdrop dismiss only
    <dialog
      ref={dialogRef}
      className="wm-dialog-overlay"
      aria-labelledby="restart-confirm-dialog-title"
      onCancel={handleCancel}
      onClick={handleClick}
    >
      <div className="wm-dialog">
        <div className="wm-dialog__title" id="restart-confirm-dialog-title">
          Restart Required
        </div>
        <div className="wm-dialog__body">
          Some of the changed settings require a restart to take effect. You can restart now or save and restart later
          on your own terms.
        </div>
        <div className="wm-dialog__actions wm-dialog__actions--restart">
          <ActionButton onClick={onCancel} disabled={isPending}>
            Cancel
          </ActionButton>
          <ActionButton variant="default" onClick={onSaveOnly} disabled={isPending}>
            {isPending ? 'Saving...' : 'Save Only'}
          </ActionButton>
          <ActionButton variant="primary" onClick={onSaveAndRestart} disabled={isPending}>
            {isPending ? 'Saving...' : 'Save & Restart'}
          </ActionButton>
        </div>
      </div>
    </dialog>
  )
}

export { RestartConfirmDialog }
export type { RestartConfirmDialogProps }
