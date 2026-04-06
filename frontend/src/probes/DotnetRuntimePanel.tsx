import type { DotnetRuntimeProbe } from '@/api/types'

type DotnetRuntimePanelProps = {
  data: DotnetRuntimeProbe
}

function ProbeRow({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="wm-probe-row">
      <span className="wm-probe-row__label">{label}</span>
      <span className="wm-probe-row__value">{children}</span>
    </div>
  )
}

function BooleanBadge({ value, trueLabel, falseLabel }: { value: boolean; trueLabel: string; falseLabel: string }) {
  const isTrue = value
  return (
    <span
      className="wm-probe-badge"
      style={{
        color: isTrue ? 'var(--wm-green)' : 'var(--wm-text-dim)',
        borderColor: isTrue ? 'var(--wm-green-border)' : 'var(--wm-border)',
        background: isTrue ? 'var(--wm-green-dim)' : 'transparent',
      }}
    >
      {isTrue ? trueLabel : falseLabel}
    </span>
  )
}

function VersionValue({ version }: { version: string }) {
  return <span className="wm-probe-version">{version}</span>
}

function DotnetRuntimePanel({ data }: DotnetRuntimePanelProps) {
  return (
    <div className="flex flex-col gap-0">
      <ProbeRow label="Target Framework">
        <VersionValue version={data.tfm} />
      </ProbeRow>
      <ProbeRow label="Runtime Version">
        <VersionValue version={data.runtimeVersion} />
      </ProbeRow>
      <ProbeRow label="ASP.NET Core">
        <BooleanBadge value={data.isAspNetCore} trueLabel="Yes" falseLabel="No" />
      </ProbeRow>
      <ProbeRow label="Self-Contained">
        <BooleanBadge value={data.isSelfContained} trueLabel="Yes" falseLabel="No" />
      </ProbeRow>
      <ProbeRow label="Server GC">
        <BooleanBadge value={data.serverGc} trueLabel="Enabled" falseLabel="Disabled" />
      </ProbeRow>
    </div>
  )
}

export { DotnetRuntimePanel, ProbeRow, BooleanBadge, VersionValue }
export type { DotnetRuntimePanelProps }
