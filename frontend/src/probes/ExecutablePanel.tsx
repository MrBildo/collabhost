import type { ExecutableProbe } from '@/api/types'
import { formatBytes } from '@/lib/format'
import { ProbeRow } from './DotnetRuntimePanel'

type ExecutablePanelProps = {
  data: ExecutableProbe
}

function formatCandidateSummary(binaryName: string, candidateBinaryCount: number): React.ReactNode {
  if (candidateBinaryCount <= 1) {
    return <span className="wm-probe-version">{binaryName}</span>
  }
  return (
    <span>
      <span className="wm-probe-version">{binaryName}</span>
      <span style={{ color: 'var(--wm-text-dim)', marginLeft: 6, fontSize: 'var(--wm-font-2xs)' }}>
        1 of {candidateBinaryCount} candidates
      </span>
    </span>
  )
}

// Soft-nudge for executable-detected-as-.NET (Bill ruling #2 on card #220).
// Single panel only -- never side-by-side with a dotnet-runtime panel; the
// hint sits inside this panel as an informational box, not a warning. The
// existing app continues to run fine as `executable`; the operator can ignore
// the hint. Re-registering as `dotnet-app` unlocks ASP.NET Core defaults
// (health-check, environment-defaults, port injection).
function DotnetNudge() {
  return (
    <div
      className="mt-2"
      style={{
        background: 'var(--wm-bg-inset)',
        border: '1px solid var(--wm-border)',
        borderLeft: '2px solid var(--wm-cyan)',
        borderRadius: 'var(--wm-radius-sm)',
        padding: '8px 10px',
        fontSize: 'var(--wm-font-xs)',
        color: 'var(--wm-text-dim)',
        lineHeight: 1.45,
      }}
    >
      <span style={{ color: 'var(--wm-text-bright)', fontWeight: 600 }}>Looks like .NET.</span> Re-registering this app
      as <span className="wm-probe-version">dotnet-app</span> enables ASP.NET Core defaults (health checks, environment
      variables, port injection).
    </div>
  )
}

function ExecutablePanel({ data }: ExecutablePanelProps) {
  return (
    <div className="flex flex-col gap-0">
      <ProbeRow label="Binary">{formatCandidateSummary(data.binaryName, data.candidateBinaryCount)}</ProbeRow>
      <ProbeRow label="Size">
        <span style={{ color: 'var(--wm-text-bright)' }}>{formatBytes(data.binarySizeBytes)}</span>
      </ProbeRow>
      {data.isManagedDotnet && <DotnetNudge />}
    </div>
  )
}

export { ExecutablePanel }
export type { ExecutablePanelProps }
