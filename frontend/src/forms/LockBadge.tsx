type LockBadgeProps = {
  reason: string
}

function LockBadge({ reason }: LockBadgeProps) {
  return (
    <span className="wm-lock-badge" title={reason}>
      {reason}
    </span>
  )
}

export { LockBadge }
export type { LockBadgeProps }
