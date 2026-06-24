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
    // Both request params that change the response (stream filter + line count)
    // are in the key so distinct fetches don't collide on one cache entry
    // (FE-QRY-02). Dropping `lines` let a later small-N fetch read back a
    // larger-N cached payload and vice versa.
    queryKey: ['apps', slug, 'logs', params?.stream ?? 'all', params?.lines ?? null],
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
      // Targeted invalidation (FE-QRY-03): refetch the app list (where the row's
      // status lives) and this app's detail — not the whole `['apps']` prefix,
      // which also nuked every OTHER app's detail and log caches. `exact: true`
      // on the list key keeps the blast radius to the surfaces this action moved.
      queryClient.invalidateQueries({ queryKey: ['apps'], exact: true })
      queryClient.invalidateQueries({ queryKey: ['apps', slug] })
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
