import { cn } from '@/lib/cn'

type FilterChipProps = {
  label: string
  isActive: boolean
  onClick: () => void
  className?: string
}

function FilterChip({ label, isActive, onClick, className }: FilterChipProps) {
  return (
    <button
      type="button"
      className={cn('wm-filter-chip', isActive && 'wm-filter-chip--active', className)}
      onClick={onClick}
      aria-pressed={isActive}
    >
      {label}
    </button>
  )
}

export { FilterChip }
export type { FilterChipProps }
