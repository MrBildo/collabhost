import { ActionButton } from '@/actions/ActionButton'
import { useEffect, useRef } from 'react'

type ConfirmDialogProps = {
  title: string
  message: string
  confirmLabel: string
  confirmVariant?: 'danger' | 'primary'
  isOpen: boolean
  isPending?: boolean
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
  onConfirm,
  onCancel,
}: ConfirmDialogProps) {
  const dialogRef = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (isOpen && dialogRef.current) {
      const firstButton = dialogRef.current.querySelector('button')
      firstButton?.focus()
    }
  }, [isOpen])

  if (!isOpen) return null

  function handleOverlayClick(e: React.MouseEvent): void {
    if (e.target === e.currentTarget) {
      onCancel()
    }
  }

  return (
    <dialog
      className="wm-dialog-overlay"
      onClick={handleOverlayClick}
      onKeyDown={(e) => {
        if (e.key === 'Escape') onCancel()
      }}
      open
      aria-labelledby="confirm-dialog-title"
    >
      <div className="wm-dialog" ref={dialogRef}>
        <div className="wm-dialog__title" id="confirm-dialog-title">
          {title}
        </div>
        <div className="wm-dialog__body">{message}</div>
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
