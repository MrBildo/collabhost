import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import type { LookupItem } from '@/types/api';

export function useAppTypes() {
  return useQuery<LookupItem[]>({
    queryKey: ['lookups', 'app-types'],
    queryFn: () => api.get<LookupItem[]>('/lookups/app-types').then((response) => response.data),
    staleTime: Infinity,
  });
}

export function useRestartPolicies() {
  return useQuery<LookupItem[]>({
    queryKey: ['lookups', 'restart-policies'],
    queryFn: () =>
      api.get<LookupItem[]>('/lookups/restart-policies').then((response) => response.data),
    staleTime: Infinity,
  });
}
