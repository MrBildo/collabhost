import { cn } from '@/lib/cn'

type ErrorBannerProps = {
  message: string
  className?: string
  onDismiss?: () => void
}

function ErrorBanner({ message, className, onDismiss }: ErrorBannerProps) {
  return (
    <div className={cn('wm-alert wm-alert--error', className)} role="alert">
      <span style={{ color: 'var(--wm-red)', flexShrink: 0, fontWeight: 600 }}>ERR</span>
      <span className="flex-1">{message}</span>
      {onDismiss && (
        <button
          type="button"
          className="wm-btn wm-btn--ghost"
          onClick={onDismiss}
          aria-label="Dismiss error"
          style={{ color: 'var(--wm-text-dim)', padding: 0 }}
        >
          x
        </button>
      )}
    </div>
  )
}

export { ErrorBanner }
export type { ErrorBannerProps }
