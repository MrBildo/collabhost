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

function useDashboardEvents(limit = 20) {
  return useQuery<DashboardEventsResponse>({
    queryKey: ['dashboard', 'events'],
    queryFn: () => getDashboardEvents(limit),
    refetchInterval: POLL_INTERVALS.dashboard,
  })
}

export { useDashboardStats, useDashboardEvents }
