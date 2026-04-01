import type { CapabilityComponentFamily } from '../types';

import { ArtifactDisplay } from './ArtifactDisplay';
import { ArtifactWidget } from './ArtifactWidget';

export const artifactFamily: CapabilityComponentFamily = {
  Widget: ArtifactWidget,
  Display: ArtifactDisplay,
};

export { ArtifactDisplay, ArtifactWidget };
