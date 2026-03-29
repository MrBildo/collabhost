import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { api } from '@/lib/api';
import type { AppDetail, LogsResponse, UpdateAppRequest } from '@/types/api';

export function useAppDetail(id: string) {
  return useQuery<AppDetail>({
    queryKey: ['apps', id, 'detail'],
    queryFn: () => api.get<AppDetail>(`/apps/${id}`).then((response) => response.data),
    staleTime: 10_000,
  });
}

export function useAppLogs(id: string) {
  return useQuery<LogsResponse>({
    queryKey: ['apps', id, 'logs'],
    queryFn: () => api.get<LogsResponse>(`/apps/${id}/logs`).then((response) => response.data),
    refetchInterval: 5000,
  });
}

export function useUpdateAppConfig(id: string) {
  const queryClient = useQueryClient();

  return useMutation<void, Error, UpdateAppRequest>({
    mutationFn: async (request) => {
      await api.put(`/apps/${id}`, request);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apps', id, 'detail'] });
      queryClient.invalidateQueries({ queryKey: ['apps'] });
    },
  });
}

export function useDeleteApp() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  return useMutation<void, Error, string>({
    mutationFn: async (id) => {
      await api.delete(`/apps/${id}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['apps'] });
      navigate('/');
    },
  });
}
