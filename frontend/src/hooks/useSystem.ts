import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import type { AppListItem, SystemStatus } from '@/types/api';

export function useSystemStatus() {
  return useQuery({
    queryKey: ['system-status'],
    queryFn: async () => {
      const response = await api.get<SystemStatus>('/status');
      return response.data;
    },
    refetchInterval: 10_000,
  });
}

export function useAppList() {
  return useQuery({
    queryKey: ['apps'],
    queryFn: async () => {
      const response = await api.get<AppListItem[]>('/apps');
      return response.data;
    },
    refetchInterval: 10_000,
  });
}
