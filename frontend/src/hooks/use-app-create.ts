import { createApp, getAppTypes, getRegistrationSchema } from '@/api/endpoints'
import type { AppTypeListItem, CreateAppRequest, CreateAppResponse, RegistrationSchema } from '@/api/types'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

function useAppTypes() {
  return useQuery<AppTypeListItem[]>({
    queryKey: ['app-types'],
    queryFn: getAppTypes,
  })
}

function useRegistrationSchema(typeSlug: string) {
  return useQuery<RegistrationSchema>({
    queryKey: ['app-types', typeSlug, 'registration'],
    queryFn: () => getRegistrationSchema(typeSlug),
    enabled: !!typeSlug,
  })
}

function useCreateApp() {
  const queryClient = useQueryClient()

  return useMutation<CreateAppResponse, Error, CreateAppRequest>({
    mutationFn: (request) => createApp(request),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apps'] })
    },
  })
}

export { useAppTypes, useRegistrationSchema, useCreateApp }
