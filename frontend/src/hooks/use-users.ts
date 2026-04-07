import { createUser, deactivateUser, fetchUsers } from '@/api/endpoints'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

function useUsers() {
  return useQuery({
    queryKey: ['users'],
    queryFn: fetchUsers,
    refetchInterval: 30_000,
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
