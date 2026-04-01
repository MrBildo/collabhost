import { useQuery } from '@tanstack/react-query';
import { api } from '@/lib/api';
import type { FilesystemBrowseResponse } from '@/types/api';

export function useFilesystemBrowse(path: string | null) {
  return useQuery<FilesystemBrowseResponse>({
    queryKey: ['filesystem', 'browse', path],
    queryFn: () =>
      api
        .get<FilesystemBrowseResponse>('/filesystem/browse', {
          params: path ? { path } : undefined,
        })
        .then((response) => response.data),
    enabled: path !== null,
    staleTime: 30_000,
    retry: false,
  });
}
