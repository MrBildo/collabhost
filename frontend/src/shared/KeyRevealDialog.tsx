import { ActionButton } from '@/actions/ActionButton'
import type { UserCreateResponse } from '@/api/types'
import { cn } from '@/lib/cn'
import { formatRole } from '@/lib/format'
import { RoleBadge } from '@/status/RoleBadge'
import { useEffect, useRef, useState } from 'react'

type KeyRevealDialogProps = {
  user: UserCreateResponse
  onDone: () => void
}

function KeyRevealDialog({ user, onDone }: KeyRevealDialogProps) {
  const dialogRef = useRef<HTMLDialogElement>(null)
  const [isCopied, setIsCopied] = useState(false)
  const [isAcknowledged, setIsAcknowledged] = useState(false)

  useEffect(() => {
    const dialog = dialogRef.current
    if (dialog && !dialog.open) {
      dialog.showModal()
    }
  }, [])

  function handleCancel(e: React.SyntheticEvent): void {
    // Block all escape / cancel events — operator must acknowledge
    e.preventDefault()
  }

  function handleCopy(): void {
    navigator.clipboard.writeText(user.authKey).catch(() => {
      // Clipboard write failed — user can still manually copy
    })
    setIsCopied(true)
    setTimeout(() => setIsCopied(false), 2000)
  }

  function handleDone(): void {
    dialogRef.current?.close()
    onDone()
  }

  return (
    <dialog ref={dialogRef} className="wm-dialog-overlay" aria-labelledby="key-reveal-title" onCancel={handleCancel}>
      <div className="wm-dialog" style={{ maxWidth: 520 }}>
        <div className="wm-dialog__title" id="key-reveal-title">
          User Created Successfully
        </div>

        <div className="wm-dialog__body">
          <div className="flex items-center gap-2 mb-4" style={{ fontSize: 'var(--wm-font-sm)' }}>
            <span style={{ color: 'var(--wm-text-dim)' }}>
              <span style={{ fontFamily: 'var(--wm-mono)' }}>{user.name}</span>
            </span>
            <RoleBadge role={user.role} />
          </div>

          <div className="wm-key-display mb-3">
            <span className="wm-key-display__value" aria-label="Authentication key">
              {user.authKey}
            </span>
            <ActionButton
              size="sm"
              variant={isCopied ? 'success' : 'default'}
              className={cn('wm-key-display__copy', isCopied && 'wm-key-display__copy--copied')}
              onClick={handleCopy}
            >
              {isCopied ? 'Copied!' : 'Copy'}
            </ActionButton>
          </div>

          <div className="mb-4 text-xs" style={{ color: 'var(--wm-text-dim)', lineHeight: 1.6 }}>
            <p className="mb-1" style={{ color: 'var(--wm-amber)', fontWeight: 600 }}>
              This key will not be shown again.
            </p>
            <p>Store it securely before closing this dialog.</p>
            <p className="mt-1">If the key is lost, deactivate this user and create a new one.</p>
          </div>

          <label className="flex items-center gap-2 cursor-pointer" style={{ fontSize: 'var(--wm-font-sm)' }}>
            <input type="checkbox" checked={isAcknowledged} onChange={(e) => setIsAcknowledged(e.target.checked)} />
            <span style={{ color: 'var(--wm-text)' }}>I have copied and saved this key</span>
          </label>
        </div>

        <div className="wm-dialog__actions">
          <span style={{ fontSize: 'var(--wm-font-2xs)', color: 'var(--wm-text-dim)' }}>
            {formatRole(user.role)} · {user.name}
          </span>
          <ActionButton variant="primary" disabled={!isAcknowledged} onClick={handleDone}>
            Done
          </ActionButton>
        </div>
      </div>
    </dialog>
  )
}

export { KeyRevealDialog }
export type { KeyRevealDialogProps }
