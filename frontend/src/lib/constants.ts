export const APP_TYPES = [
  { id: 'acdb6994-2c22-42f5-bf89-68c42c9f980c', name: 'Executable' },
  { id: 'd71d5599-bad3-4b28-8920-1aae916bd3cb', name: 'NpmPackage' },
  { id: '7dc8cc9f-1600-447a-85f4-cbc0fc44e6fc', name: 'StaticSite' },
] as const;

export const RESTART_POLICIES = [
  { id: '2f2f6115-b6ef-4db4-b3c7-200a4dbb3408', name: 'Never' },
  { id: 'a5806eba-9dcd-4145-acc3-7bcabd699829', name: 'OnCrash' },
  { id: '3902811f-674d-483a-9d6b-8b8917d83c0f', name: 'Always' },
] as const;
