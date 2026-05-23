type DependencyBadgeProps = {
  parentLabel: string
  requiredValueLabel: string
}

// Card #338 -- effectiveness-predicate badge. Renders alongside Lock/Derived/
// Override/Restart in the badge family on a field whose schema-declared
// DependsOn parent does not currently equal the required value. Visual treatment
// matches LockBadge (dim text on inset background) -- the field is here, but
// its value will not take effect right now.
function DependencyBadge({ parentLabel, requiredValueLabel }: DependencyBadgeProps) {
  const reason = `Effective only when ${parentLabel} = ${requiredValueLabel}`
  return (
    <span className="wm-dependency-badge" title={reason}>
      {reason}
    </span>
  )
}

export { DependencyBadge }
export type { DependencyBadgeProps }
