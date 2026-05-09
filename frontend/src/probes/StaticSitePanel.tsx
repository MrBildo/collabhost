import type { StaticSiteProbe } from '@/api/types'
import { formatBytes } from '@/lib/format'
import { BooleanBadge, ProbeRow } from './DotnetRuntimePanel'

type StaticSitePanelProps = {
  data: StaticSiteProbe
}

// The backend caps total asset size at 200 MB. When at the cap, we render
// the size as "200 MB+" rather than a precise byte count -- the precise
// number isn't truthful past the walk threshold and the cap has already
// answered the operational question ("this site has a lot of assets").
const ASSET_BYTES_CAP = 200 * 1024 * 1024

function formatAssetSize(totalAssetBytes: number): string {
  if (totalAssetBytes >= ASSET_BYTES_CAP) return '200 MB+'
  return formatBytes(totalAssetBytes)
}

function StaticSitePanel({ data }: StaticSitePanelProps) {
  return (
    <div className="flex flex-col gap-0">
      <ProbeRow label="Index">
        <BooleanBadge value={data.hasIndexHtml} trueLabel="index.html" falseLabel="None" />
      </ProbeRow>
      <ProbeRow label="HTML Files">
        <span style={{ color: 'var(--wm-text-bright)' }}>{data.htmlFileCount}</span>
      </ProbeRow>
      <ProbeRow label="Asset Size">
        <span style={{ color: 'var(--wm-text-bright)' }}>{formatAssetSize(data.totalAssetBytes)}</span>
      </ProbeRow>
      <ProbeRow label="Asset Layout">
        <BooleanBadge value={data.hasNestedAssets} trueLabel="Nested" falseLabel="Flat" />
      </ProbeRow>
    </div>
  )
}

export { StaticSitePanel }
export type { StaticSitePanelProps }
