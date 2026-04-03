import type { AppActions } from '@/api/types'
import { ActionButton } from './ActionButton'

type ActionBarProps = {
  actions: AppActions
  isTransitioning: boolean
  onStart: () => void
  onStop: () => void
  onRestart: () => void
  onKill: () => void
  onUpdate: () => void
}

function ActionBar({ actions, isTransitioning, onStart, onStop, onRestart, onKill, onUpdate }: ActionBarProps) {
  return (
    <div className="flex items-center gap-2">
      {actions.canStart && (
        <ActionButton variant="success" size="sm" disabled={isTransitioning} onClick={onStart}>
          Start
        </ActionButton>
      )}
      {actions.canStop && (
        <ActionButton variant="default" size="sm" disabled={isTransitioning} onClick={onStop}>
          Stop
        </ActionButton>
      )}
      {actions.canRestart && (
        <ActionButton variant="default" size="sm" disabled={isTransitioning} onClick={onRestart}>
          Restart
        </ActionButton>
      )}
      {actions.canKill && (
        <ActionButton variant="warn" size="sm" disabled={isTransitioning} onClick={onKill}>
          Kill
        </ActionButton>
      )}
      {actions.canUpdate && (
        <ActionButton variant="amber" size="sm" disabled={isTransitioning} onClick={onUpdate}>
          Update
        </ActionButton>
      )}
    </div>
  )
}

export { ActionBar }
export type { ActionBarProps }
