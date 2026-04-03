import { getRoutes, reloadProxy } from '@/api/endpoints'
import type { RouteListResponse } from '@/api/types'
import { POLL_INTERVALS } from '@/lib/constants'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

function useRoutes() {
  return useQuery<RouteListResponse>({
    queryKey: ['routes'],
    queryFn: getRoutes,
    refetchInterval: POLL_INTERVALS.apps,
  })
}

function useReloadProxy() {
  const queryClient = useQueryClient()

  return useMutation<void, Error>({
    mutationFn: () => reloadProxy(),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['routes'] })
    },
  })
}

export { useRoutes, useReloadProxy }
