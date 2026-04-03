import { cn } from '@/lib/cn'
import type { ReactNode } from 'react'

type ActionButtonVariant = 'default' | 'amber' | 'primary' | 'danger' | 'success' | 'warn' | 'ghost'
type ActionButtonSize = 'sm' | 'md' | 'lg'

type ActionButtonProps = {
  children: ReactNode
  variant?: ActionButtonVariant
  size?: ActionButtonSize
  disabled?: boolean
  onClick?: () => void
  className?: string
  type?: 'button' | 'submit'
}

const VARIANT_CLASSES: Record<ActionButtonVariant, string> = {
  default: '',
  amber: 'wm-btn--amber',
  primary: 'wm-btn--primary',
  danger: 'wm-btn--danger',
  success: 'wm-btn--success',
  warn: 'wm-btn--warn',
  ghost: 'wm-btn--ghost',
}

const SIZE_CLASSES: Record<ActionButtonSize, string> = {
  sm: 'wm-btn--sm',
  md: '',
  lg: 'wm-btn--lg',
}

function ActionButton({
  children,
  variant = 'default',
  size = 'md',
  disabled = false,
  onClick,
  className,
  type = 'button',
}: ActionButtonProps) {
  return (
    <button
      type={type}
      className={cn('wm-btn', VARIANT_CLASSES[variant], SIZE_CLASSES[size], className)}
      disabled={disabled}
      onClick={onClick}
    >
      {children}
    </button>
  )
}

export { ActionButton }
export type { ActionButtonProps, ActionButtonVariant, ActionButtonSize }
