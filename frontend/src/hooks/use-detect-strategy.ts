import { detectStrategy } from '@/api/endpoints'
import type { DetectStrategyResponse } from '@/api/types'
import { useQuery } from '@tanstack/react-query'

function useDetectStrategy(path: string, appTypeSlug: string) {
  return useQuery<DetectStrategyResponse>({
    queryKey: ['filesystem', 'detect-strategy', path, appTypeSlug],
    queryFn: () => detectStrategy(path, appTypeSlug),
    enabled: !!path && !!appTypeSlug,
    staleTime: 60_000,
    retry: false,
  })
}

export { useDetectStrategy }
