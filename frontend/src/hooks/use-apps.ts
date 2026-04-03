import { getApps, startApp, stopApp } from '@/api/endpoints'
import type { ActionResult, AppDetail, AppListItem } from '@/api/types'
import { POLL_INTERVALS } from '@/lib/constants'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

function useApps() {
  return useQuery<AppListItem[]>({
    queryKey: ['apps'],
    queryFn: getApps,
    refetchInterval: POLL_INTERVALS.apps,
  })
}

function useStartApp() {
  const queryClient = useQueryClient()

  return useMutation<ActionResult, Error, string>({
    mutationFn: (slug: string) => startApp(slug),
    onSuccess: (result, slug) => {
      queryClient.setQueryData<AppDetail>(['apps', slug], (old) =>
        old ? { ...old, status: result.status, actions: result.actions } : old,
      )
      queryClient.invalidateQueries({ queryKey: ['apps'] })
    },
  })
}

function useStopApp() {
  const queryClient = useQueryClient()

  return useMutation<ActionResult, Error, string>({
    mutationFn: (slug: string) => stopApp(slug),
    onSuccess: (result, slug) => {
      queryClient.setQueryData<AppDetail>(['apps', slug], (old) =>
        old ? { ...old, status: result.status, actions: result.actions } : old,
      )
      queryClient.invalidateQueries({ queryKey: ['apps'] })
    },
  })
}

export { useApps, useStartApp, useStopApp }
