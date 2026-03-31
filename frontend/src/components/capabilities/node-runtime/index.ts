import type { CapabilityComponentFamily } from '../types';

import { NodeRuntimeDisplay } from './NodeRuntimeDisplay';

export const nodeRuntimeFamily: CapabilityComponentFamily = {
  Widget: NodeRuntimeDisplay,
  Display: NodeRuntimeDisplay,
};

export { NodeRuntimeDisplay };
