import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api';
import type {
  AppTypeDetail,
  AppTypeListItem,
  CreateAppTypeRequest,
  CreateAppTypeResponse,
  UpdateAppTypeRequest,
} from '@/types/api';

export function useAppTypeList() {
  return useQuery<AppTypeListItem[]>({
    queryKey: ['app-types'],
    queryFn: () => api.get<AppTypeListItem[]>('/app-types').then((response) => response.data),
    staleTime: 30_000,
  });
}

export function useAppTypeDetail(externalId: string) {
  return useQuery<AppTypeDetail>({
    queryKey: ['app-types', externalId],
    queryFn: () =>
      api.get<AppTypeDetail>(`/app-types/${externalId}`).then((response) => response.data),
    staleTime: 10_000,
  });
}

export function useCreateAppType() {
  const queryClient = useQueryClient();

  return useMutation<CreateAppTypeResponse, Error, CreateAppTypeRequest>({
    mutationFn: (request) =>
      api.post<CreateAppTypeResponse>('/app-types', request).then((response) => response.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['app-types'] });
    },
  });
}

export function useUpdateAppType(externalId: string) {
  const queryClient = useQueryClient();

  return useMutation<void, Error, UpdateAppTypeRequest>({
    mutationFn: async (request) => {
      await api.put(`/app-types/${externalId}`, request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['app-types', externalId] });
      queryClient.invalidateQueries({ queryKey: ['app-types'] });
    },
  });
}

export function useDeleteAppType() {
  const queryClient = useQueryClient();

  return useMutation<void, Error, string>({
    mutationFn: async (externalId) => {
      await api.delete(`/app-types/${externalId}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['app-types'] });
    },
  });
}
