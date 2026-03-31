import { clsx, type ClassValue } from 'clsx';
import { twMerge } from 'tailwind-merge';

import type { AppCapabilityResponse } from '@/types/api';
import type { CapabilityEntry } from '@/components/capabilities/types';

export function cn(...inputs: ClassValue[]) {
  return twMerge(clsx(inputs));
}

/** Converts API capability dict to the CapabilityEntry[] the capability components expect. */
export function toCapabilityEntries(
  capabilities: Record<string, AppCapabilityResponse>,
): CapabilityEntry[] {
  return Object.entries(capabilities).map(([slug, cap]) => ({
    slug,
    category: cap.category as 'behavioral' | 'informational',
    displayName: cap.displayName,
    resolved: cap.resolved,
    defaults: cap.defaults,
    hasOverrides: cap.hasOverrides,
  }));
}
