import { cn } from '@/lib/cn'

type SpinnerProps = {
  className?: string
}

function Spinner({ className }: SpinnerProps) {
  return <div className={cn('wm-loading-bar', className)} aria-label="Loading" />
}

export { Spinner }
export type { SpinnerProps }
