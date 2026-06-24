import { deleteApp, getAppSettings, importRuntimeConfigFile, restartApp, updateAppSettings } from '@/api/endpoints'
import type { ActionResult, AppSettings, RuntimeConfigFileImportResponse, UpdateSettingsRequest } from '@/api/types'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

function useAppSettings(slug: string) {
  return useQuery<AppSettings>({
    queryKey: ['apps', slug, 'settings'],
    queryFn: () => getAppSettings(slug),
    enabled: !!slug,
  })
}

function useSaveSettings(slug: string) {
  const queryClient = useQueryClient()

  return useMutation<AppSettings, Error, UpdateSettingsRequest>({
    mutationFn: (request) => updateAppSettings(slug, request),
    onSuccess: (updatedSettings) => {
      queryClient.setQueryData(['apps', slug, 'settings'], updatedSettings)
      queryClient.invalidateQueries({ queryKey: ['apps', slug] })
      queryClient.invalidateQueries({ queryKey: ['apps'] })
    },
  })
}

function useDeleteApp() {
  const queryClient = useQueryClient()

  return useMutation<void, Error, string>({
    mutationFn: (slug) => deleteApp(slug),
    onSuccess: (_result, slug) => {
      // #409: on delete the AppSettingsPage's useAppSettings(slug) observer is
      // still mounted and subscribed while the page redirects to /apps. Two cache
      // operations would re-fetch the just-deleted app's /settings against the now
      // 404ing resource, in the window before the redirect unmounts the observer:
      //   - removeQueries(['apps', slug]) destroys the query, and the still-active
      //     observer rebuilds + refetches it on the next render (verified directly:
      //     removeQueries + a render of a live observer fires a fresh fetch).
      //   - a bare invalidateQueries(['apps']) (non-exact) marks the still-active
      //     ['apps', slug, 'settings'] query stale and refetches it.
      // The safe shape: cancel any in-flight per-app fetch, then invalidate ONLY
      // the list (exact) so the deleted app's per-app observers are never touched.
      // The deleted app's now-observerless detail/settings entries are evicted by
      // gc after the redirect unmounts the page — actively removing them while the
      // observer is live is exactly what triggered the 404.
      queryClient.cancelQueries({ queryKey: ['apps', slug] })
      queryClient.invalidateQueries({ queryKey: ['apps'], exact: true })
    },
  })
}

// Card #336. Preview-only import — the response is staged into a confirmation
// modal and merged into the local edit values on operator confirm. No query
// invalidation here: nothing on the server changed.
function useImportRuntimeConfigFile(slug: string) {
  return useMutation<RuntimeConfigFileImportResponse, Error, void>({
    mutationFn: () => importRuntimeConfigFile(slug),
  })
}

function useSettingsRestartApp(slug: string) {
  const queryClient = useQueryClient()

  return useMutation<ActionResult, Error, void>({
    mutationFn: () => restartApp(slug),
    onSuccess: (result) => {
      queryClient.setQueryData(['apps', slug], (old: unknown) =>
        old && typeof old === 'object' ? { ...old, status: result.status, actions: result.actions } : old,
      )
      queryClient.invalidateQueries({ queryKey: ['apps'] })
    },
  })
}

export { useAppSettings, useSaveSettings, useDeleteApp, useImportRuntimeConfigFile, useSettingsRestartApp }
