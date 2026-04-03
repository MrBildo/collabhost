import { deleteApp, getAppSettings, updateAppSettings } from '@/api/endpoints'
import type { AppSettings, UpdateSettingsRequest } from '@/api/types'
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
      queryClient.removeQueries({ queryKey: ['apps', slug] })
      queryClient.invalidateQueries({ queryKey: ['apps'] })
    },
  })
}

export { useAppSettings, useSaveSettings, useDeleteApp }
