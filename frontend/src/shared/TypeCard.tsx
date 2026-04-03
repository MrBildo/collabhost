import type { AppTag } from '@/api/types'
import { cn } from '@/lib/cn'

type TypeCardProps = {
  name: string
  displayName: string
  description: string | null
  tags: AppTag[]
  isSelected: boolean
  onClick: () => void
}

function TypeCard({ displayName, description, tags, isSelected, onClick }: TypeCardProps) {
  return (
    <button
      type="button"
      className={cn('wm-type-card', isSelected && 'wm-type-card--selected')}
      onClick={onClick}
      style={{ textAlign: 'left', width: '100%' }}
    >
      <div className="wm-type-card__name">{displayName}</div>
      {description && <div className="wm-type-card__desc">{description}</div>}
      {tags.length > 0 && (
        <div className="wm-type-card__tags">
          {tags.map((tag) => (
            <span key={tag.label} className="wm-type-card__tag">
              {tag.label}
            </span>
          ))}
        </div>
      )}
    </button>
  )
}

export { TypeCard }
export type { TypeCardProps }
