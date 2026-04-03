import type { AppStatus, HealthStatus } from '@/api/types'

function formatUptime(seconds: number | null | undefined): string {
  if (seconds == null) return '--'
  if (seconds < 60) return `${Math.round(seconds)}s`
  if (seconds < 3600) return `${Math.floor(seconds / 60)}m`
  if (seconds < 86400) return `${Math.floor(seconds / 3600)}h`
  return `${Math.floor(seconds / 86400)}d`
}

function formatUptimeLong(seconds: number | null | undefined): string {
  if (seconds == null) return '--'
  const totalSeconds = Math.round(seconds)
  const days = Math.floor(totalSeconds / 86400)
  const hours = Math.floor((totalSeconds % 86400) / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  const secs = totalSeconds % 60

  if (days > 0) return `${days}d ${hours}h`
  if (hours > 0) return `${hours}h ${minutes}m`
  if (minutes > 0) return `${minutes}m ${secs}s`
  return `${secs}s`
}

const STATUS_LABELS: Record<AppStatus, string> = {
  running: 'Running',
  stopped: 'Stopped',
  crashed: 'Crashed',
  starting: 'Starting',
  stopping: 'Stopping',
  restarting: 'Restarting',
}

function formatStatus(status: AppStatus): string {
  return STATUS_LABELS[status] ?? status
}

const HEALTH_LABELS: Record<HealthStatus, string> = {
  healthy: 'Healthy',
  unhealthy: 'Unhealthy',
  degraded: 'Degraded',
  unknown: 'Unknown',
}

function formatHealthStatus(status: HealthStatus): string {
  return HEALTH_LABELS[status] ?? status
}

function formatEnumLabel(value: string): string {
  return value.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase())
}

function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return '--'
  const date = new Date(iso)
  return date.toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatTimestamp(iso: string): string {
  const date = new Date(iso)
  return date.toLocaleTimeString(undefined, {
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

function formatMemory(mb: number | null | undefined): string {
  if (mb == null) return '--'
  if (mb >= 1024) return `${(mb / 1024).toFixed(1)} GB`
  return `${Math.round(mb)} MB`
}

export {
  formatUptime,
  formatUptimeLong,
  formatStatus,
  formatHealthStatus,
  formatEnumLabel,
  formatDateTime,
  formatTimestamp,
  formatMemory,
}
