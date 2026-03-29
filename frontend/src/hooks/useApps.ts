import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '@/lib/api';
import type { AppListItem, ProcessStatus } from '@/types/api';

export function useApps() {
  return useQuery<AppListItem[]>({
    queryKey: ['apps'],
    queryFn: () => api.get<AppListItem[]>('/apps').then((response) => response.data),
    refetchInterval: 5000,
  });
}

export function useAppStatus(id: string) {
  return useQuery<ProcessStatus | null>({
    queryKey: ['apps', id, 'status'],
    queryFn: async () => {
      try {
        const response = await api.get<ProcessStatus>(`/apps/${id}/status`);
        return response.data;
      } catch (error) {
        if (isAxios404(error)) {
          return null;
        }
        throw error;
      }
    },
    refetchInterval: 5000,
  });
}

export function useStartApp() {
  const queryClient = useQueryClient();

  return useMutation<ProcessStatus, Error, string>({
    mutationFn: (id) =>
      api.post<ProcessStatus>(`/apps/${id}/start`).then((response) => response.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apps'] });
    },
  });
}

export function useStopApp() {
  const queryClient = useQueryClient();

  return useMutation<ProcessStatus, Error, string>({
    mutationFn: (id) =>
      api.post<ProcessStatus>(`/apps/${id}/stop`).then((response) => response.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apps'] });
    },
  });
}

export function useRestartApp() {
  const queryClient = useQueryClient();

  return useMutation<ProcessStatus, Error, string>({
    mutationFn: (id) =>
      api.post<ProcessStatus>(`/apps/${id}/restart`).then((response) => response.data),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apps'] });
    },
  });
}

function isAxios404(error: unknown): boolean {
  return (
    typeof error === 'object' &&
    error !== null &&
    'response' in error &&
    typeof (error as { response?: { status?: number } }).response?.status === 'number' &&
    (error as { response: { status: number } }).response.status === 404
  );
}
