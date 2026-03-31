import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import type { CapabilityCatalogItem } from '@/types/api';

export function useCapabilityCatalog() {
  return useQuery<CapabilityCatalogItem[]>({
    queryKey: ['capabilities'],
    queryFn: () =>
      api.get<CapabilityCatalogItem[]>('/capabilities').then((response) => response.data),
    staleTime: Infinity,
  });
}
