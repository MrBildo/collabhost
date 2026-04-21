import { getSystemStatus } from '@/api/endpoints'
import type { SystemStatus } from '@/api/types'
import { POLL_INTERVALS } from '@/lib/constants'
import { useQuery } from '@tanstack/react-query'

type UseSystemStatusOptions = {
  refetchInterval?: number
}

function useSystemStatus(options?: UseSystemStatusOptions) {
  return useQuery<SystemStatus>({
    queryKey: ['system-status'],
    queryFn: getSystemStatus,
    refetchInterval: options?.refetchInterval ?? POLL_INTERVALS.system,
  })
}

export { useSystemStatus }
export type { UseSystemStatusOptions }
