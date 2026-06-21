import { ActionButton } from '@/actions/ActionButton'
import { ErrorBanner } from '@/shared/ErrorBanner'
import { useEffect, useRef } from 'react'

type ConfirmDialogProps = {
  title: string
  message: string
  confirmLabel: string
  confirmVariant?: 'danger' | 'primary'
  isOpen: boolean
  isPending?: boolean
  // When the confirmed mutation fails, the caller leaves the dialog open and
  // passes the failure message here so the operator sees why nothing happened
  // (FE-FORM-03 / #101 class — failed mutations must not close or no-op
  // silently). Reuses the inline ErrorBanner pattern; no new design surface.
  error?: string | null
  onConfirm: () => void
  onCancel: () => void
}

function ConfirmDialog({
  title,
  message,
  confirmLabel,
  confirmVariant = 'danger',
  isOpen,
  isPending = false,
  error,
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
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
    // so we route through onCancel for consistent behavior
    e.preventDefault()
    onCancel()
  }

  function handleClick(e: React.MouseEvent<HTMLDialogElement>): void {
    // Click on the backdrop (the dialog element itself, not its children)
    // closes the dialog. The ::backdrop pseudo-element doesn't receive
    // click events directly, but clicks on it hit the dialog element.
    if (e.target === dialogRef.current) {
      onCancel()
    }
  }

  return (
    // biome-ignore lint/a11y/useKeyWithClickEvents: native dialog handles Escape via onCancel; onClick is for backdrop dismiss only
    <dialog
      ref={dialogRef}
      className="wm-dialog-overlay"
      aria-labelledby="confirm-dialog-title"
      onCancel={handleCancel}
      onClick={handleClick}
    >
      <div className="wm-dialog">
        <div className="wm-dialog__title" id="confirm-dialog-title">
          {title}
        </div>
        <div className="wm-dialog__body">{message}</div>
        {error && <ErrorBanner message={error} className="mb-4" />}
        <div className="wm-dialog__actions">
          <ActionButton onClick={onCancel} disabled={isPending}>
            Cancel
          </ActionButton>
          <ActionButton variant={confirmVariant} onClick={onConfirm} disabled={isPending}>
            {isPending ? 'Working...' : confirmLabel}
          </ActionButton>
        </div>
      </div>
    </dialog>
  )
}

export { ConfirmDialog }
export type { ConfirmDialogProps }
