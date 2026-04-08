import { createUser, deactivateUser, fetchUsers } from '@/api/endpoints'
import { POLL_INTERVALS } from '@/lib/constants'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

function useUsers() {
  return useQuery({
    queryKey: ['users'],
    queryFn: fetchUsers,
    refetchInterval: POLL_INTERVALS.users,
  })
}

function useCreateUser() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: createUser,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] })
    },
  })
}

function useDeactivateUser() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: deactivateUser,
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['users'] })
    },
  })
}

export { useUsers, useCreateUser, useDeactivateUser }
