import type { ProcessState } from '@/types/api';

export const BASE_DOMAIN = 'collab.internal';

export type StatusConfig = {
  color: string;
  label: string;
};

export const STATUS_MAP: Record<ProcessState, StatusConfig> = {
  Running: { color: 'bg-green-500', label: 'Running' },
  Stopped: { color: 'bg-gray-400', label: 'Stopped' },
  Crashed: { color: 'bg-red-500', label: 'Crashed' },
  Starting: { color: 'bg-amber-400', label: 'Starting' },
  Stopping: { color: 'bg-amber-400', label: 'Stopping' },
  Restarting: { color: 'bg-amber-400', label: 'Restarting' },
  Unknown: { color: 'bg-gray-400', label: 'Unknown' },
};

/** App type display names returned by the API */
export const APP_TYPE_NAMES = {
  STATIC_SITE: 'Static Site',
} as const;
