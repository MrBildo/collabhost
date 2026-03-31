import type { CapabilityComponentFamily } from '../types';

import { AspnetRuntimeDisplay } from './AspnetRuntimeDisplay';

export const aspnetRuntimeFamily: CapabilityComponentFamily = {
  Widget: AspnetRuntimeDisplay,
  Display: AspnetRuntimeDisplay,
};

export { AspnetRuntimeDisplay };
