import { useQuery } from '@tanstack/react-query';
import { useCallback, useMemo } from 'react';

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

export function useServeModes() {
  return useQuery<LookupItem[]>({
    queryKey: ['lookups', 'serve-modes'],
    queryFn: () => api.get<LookupItem[]>('/lookups/serve-modes').then((response) => response.data),
    staleTime: Infinity,
  });
}

export function useDiscoveryStrategies() {
  return useQuery<LookupItem[]>({
    queryKey: ['lookups', 'discovery-strategies'],
    queryFn: () =>
      api.get<LookupItem[]>('/lookups/discovery-strategies').then((response) => response.data),
    staleTime: Infinity,
  });
}

/**
 * Returns a display label resolver for a lookup query result.
 * Finds the displayName for a given name value, or falls back to the raw value.
 */
export function useLookupLabel(items: LookupItem[] | undefined) {
  const getDisplayLabel = useCallback(
    (name: string): string => {
      if (!items) return name;
      const match = items.find((item) => item.name === name);
      return match?.displayName ?? name;
    },
    [items],
  );

  const options = useMemo(
    () =>
      (items ?? []).map((item) => ({
        value: item.name,
        displayName: item.displayName,
      })),
    [items],
  );

  return { getDisplayLabel, options };
}
