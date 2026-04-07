import { fetchMe } from '@/api/endpoints'
import { useAuth } from '@/hooks/use-auth'
import { useQuery } from '@tanstack/react-query'

function useCurrentUser() {
  const { isAuthenticated } = useAuth()

  return useQuery({
    queryKey: ['auth', 'me'],
    queryFn: fetchMe,
    enabled: isAuthenticated,
    staleTime: Number.POSITIVE_INFINITY,
    retry: false,
  })
}

export { useCurrentUser }
