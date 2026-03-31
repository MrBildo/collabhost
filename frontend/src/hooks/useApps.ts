import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api';
import type { AppResponse, CreateAppRequest, CreateAppResponse } from '@/types/api';

export function useApps() {
  return useQuery<AppResponse[]>({
    queryKey: ['apps'],
    queryFn: () => api.get<AppResponse[]>('/apps').then((response) => response.data),
    refetchInterval: 5000,
  });
}

export function useStartApp() {
  const queryClient = useQueryClient();

  return useMutation<AppResponse, Error, string>({
    mutationFn: (id) =>
      api.post<AppResponse>(`/apps/${id}/start`).then((response) => response.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apps'] });
    },
  });
}

export function useStopApp() {
  const queryClient = useQueryClient();

  return useMutation<AppResponse, Error, string>({
    mutationFn: (id) => api.post<AppResponse>(`/apps/${id}/stop`).then((response) => response.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apps'] });
    },
  });
}

export function useRestartApp() {
  const queryClient = useQueryClient();

  return useMutation<AppResponse, Error, string>({
    mutationFn: (id) =>
      api.post<AppResponse>(`/apps/${id}/restart`).then((response) => response.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apps'] });
    },
  });
}

export function useKillApp() {
  const queryClient = useQueryClient();

  return useMutation<AppResponse, Error, string>({
    mutationFn: (id) => api.post<AppResponse>(`/apps/${id}/kill`).then((response) => response.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apps'] });
    },
  });
}

export function useCreateApp() {
  const queryClient = useQueryClient();

  return useMutation<CreateAppResponse, Error, CreateAppRequest>({
    mutationFn: (request) =>
      api.post<CreateAppResponse>('/apps', request).then((response) => response.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apps'] });
    },
  });
}
