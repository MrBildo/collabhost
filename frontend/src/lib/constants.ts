export const APP_TYPES = [
  { id: 'acdb6994-2c22-42f5-bf89-68c42c9f980c', name: 'Executable', displayName: 'Executable' },
  { id: 'd71d5599-bad3-4b28-8920-1aae916bd3cb', name: 'NpmPackage', displayName: 'NPM Package' },
  { id: '7dc8cc9f-1600-447a-85f4-cbc0fc44e6fc', name: 'StaticSite', displayName: 'Static Site' },
] as const;

export const RESTART_POLICIES = [
  { id: '2f2f6115-b6ef-4db4-b3c7-200a4dbb3408', name: 'Never', displayName: 'Never' },
  { id: 'a5806eba-9dcd-4145-acc3-7bcabd699829', name: 'OnCrash', displayName: 'On Crash' },
  { id: '3902811f-674d-483a-9d6b-8b8917d83c0f', name: 'Always', displayName: 'Always' },
] as const;

/** Map a backend Name value to its user-facing DisplayName */
export function appTypeDisplayName(name: string): string {
  return APP_TYPES.find((t) => t.name === name)?.displayName ?? name;
}

/** Map a backend Name value to its user-facing DisplayName */
export function restartPolicyDisplayName(name: string): string {
  return RESTART_POLICIES.find((p) => p.name === name)?.displayName ?? name;
}
