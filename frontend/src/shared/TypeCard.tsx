import { cn } from '@/lib/cn'

type TypeCardProps = {
  name: string
  displayName: string
  description: string | null
  isSelected: boolean
  onClick: () => void
}

function TypeCard({ displayName, description, isSelected, onClick }: TypeCardProps) {
  return (
    <button
      type="button"
      className={cn('wm-type-card', isSelected && 'wm-type-card--selected')}
      onClick={onClick}
      style={{ textAlign: 'left', width: '100%' }}
    >
      <div className="wm-type-card__name">{displayName}</div>
      {description && <div className="wm-type-card__desc">{description}</div>}
    </button>
  )
}

export { TypeCard }
export type { TypeCardProps }
