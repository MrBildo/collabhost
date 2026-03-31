import type { CapabilityComponentFamily } from '../types';

import { ReactRuntimeDisplay } from './ReactRuntimeDisplay';

export const reactRuntimeFamily: CapabilityComponentFamily = {
  Widget: ReactRuntimeDisplay,
  Display: ReactRuntimeDisplay,
};

export { ReactRuntimeDisplay };
