import { getDashboardEvents, getDashboardStats } from '@/api/endpoints'
import type { DashboardEventsResponse, DashboardStats } from '@/api/types'
import { POLL_INTERVALS } from '@/lib/constants'
import { useQuery } from '@tanstack/react-query'

function useDashboardStats() {
  return useQuery<DashboardStats>({
    queryKey: ['dashboard', 'stats'],
    queryFn: getDashboardStats,
    refetchInterval: POLL_INTERVALS.dashboard,
  })
}

// On error, back off rather than latch off — keep polling at a slower cadence
// so a transient blip self-recovers (FE-QRY-01). Returning `false` here would
// disable polling permanently and (with refetchOnWindowFocus default) leave the
// feed dark until a full page reload.
function dashboardEventsRefetchInterval(status: 'pending' | 'error' | 'success'): number {
  return status === 'error' ? POLL_INTERVALS.dashboardErrorBackoff : POLL_INTERVALS.dashboard
}

function useDashboardEvents(limit = 20) {
  return useQuery<DashboardEventsResponse>({
    queryKey: ['dashboard', 'events'],
    queryFn: () => getDashboardEvents(limit),
    refetchInterval: (query) => dashboardEventsRefetchInterval(query.state.status),
  })
}

export { useDashboardStats, useDashboardEvents, dashboardEventsRefetchInterval }
