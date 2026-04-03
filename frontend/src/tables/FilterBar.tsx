import type { AppStatus } from '@/api/types'
import { cn } from '@/lib/cn'
import { FilterChip } from '@/shared/FilterChip'

type StatusFilter = AppStatus | 'all'

type FilterBarProps = {
  activeFilter: StatusFilter
  onFilterChange: (filter: StatusFilter) => void
  searchTerm: string
  onSearchChange: (term: string) => void
  className?: string
}

const STATUS_FILTERS: { value: StatusFilter; label: string }[] = [
  { value: 'all', label: 'All' },
  { value: 'running', label: 'Running' },
  { value: 'stopped', label: 'Stopped' },
  { value: 'crashed', label: 'Crashed' },
]

function FilterBar({ activeFilter, onFilterChange, searchTerm, onSearchChange, className }: FilterBarProps) {
  return (
    <div className={cn('flex items-center gap-3', className)}>
      <div className="flex items-center gap-1.5">
        {STATUS_FILTERS.map((f) => (
          <FilterChip
            key={f.value}
            label={f.label}
            isActive={activeFilter === f.value}
            onClick={() => onFilterChange(f.value)}
          />
        ))}
      </div>
      <input
        type="text"
        className="wm-input"
        placeholder="Search apps..."
        value={searchTerm}
        onChange={(e) => onSearchChange(e.target.value)}
        style={{ maxWidth: 220 }}
      />
    </div>
  )
}

export { FilterBar }
export type { FilterBarProps, StatusFilter }
