import { getAppDetail, getAppLogs, killApp, restartApp, startApp, stopApp } from '@/api/endpoints'
import type { ActionResult, AppDetail, LogsResponse } from '@/api/types'
import { POLL_INTERVALS } from '@/lib/constants'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

function useAppDetail(slug: string) {
  return useQuery<AppDetail>({
    queryKey: ['apps', slug],
    queryFn: () => getAppDetail(slug),
    refetchInterval: POLL_INTERVALS.appDetail,
    enabled: !!slug,
  })
}

function useAppLogs(
  slug: string,
  params?: { lines?: number; stream?: 'all' | 'stdout' | 'stderr'; enabled?: boolean },
) {
  return useQuery<LogsResponse>({
    queryKey: ['apps', slug, 'logs', params?.stream ?? 'all'],
    queryFn: () => getAppLogs(slug, params),
    refetchInterval: POLL_INTERVALS.logs,
    enabled: (params?.enabled ?? true) && !!slug,
  })
}

function useAppAction(actionFn: (slug: string) => Promise<ActionResult>) {
  const queryClient = useQueryClient()

  return useMutation<ActionResult, Error, string>({
    mutationFn: actionFn,
    onSuccess: (result, slug) => {
      queryClient.setQueryData<AppDetail>(['apps', slug], (old) =>
        old ? { ...old, status: result.status, actions: result.actions } : old,
      )
      queryClient.invalidateQueries({ queryKey: ['apps'] })
    },
  })
}

function useDetailStartApp() {
  return useAppAction(startApp)
}

function useDetailStopApp() {
  return useAppAction(stopApp)
}

function useDetailRestartApp() {
  return useAppAction(restartApp)
}

function useDetailKillApp() {
  return useAppAction(killApp)
}

export { useAppDetail, useAppLogs, useDetailStartApp, useDetailStopApp, useDetailRestartApp, useDetailKillApp }
