import { browseFilesystem } from '@/api/endpoints'
import type { FilesystemBrowseResponse } from '@/api/types'
import { useQuery } from '@tanstack/react-query'

function useBrowseDirectories(path: string, enabled: boolean) {
  return useQuery<FilesystemBrowseResponse>({
    queryKey: ['filesystem', 'browse', path],
    queryFn: () => browseFilesystem(path),
    enabled,
    staleTime: 30_000,
  })
}

export { useBrowseDirectories }
