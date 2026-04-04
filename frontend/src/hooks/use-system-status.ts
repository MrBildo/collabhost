import { getSystemStatus } from '@/api/endpoints'
import type { SystemStatus } from '@/api/types'
import { POLL_INTERVALS } from '@/lib/constants'
import { useQuery } from '@tanstack/react-query'

function useSystemStatus() {
  return useQuery<SystemStatus>({
    queryKey: ['system-status'],
    queryFn: getSystemStatus,
    refetchInterval: POLL_INTERVALS.system,
  })
}

export { useSystemStatus }
