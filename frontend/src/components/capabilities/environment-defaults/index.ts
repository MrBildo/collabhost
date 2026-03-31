import type { CapabilityComponentFamily } from '../types';

import { EnvironmentDefaultsDisplay } from './EnvironmentDefaultsDisplay';
import { EnvironmentDefaultsWidget } from './EnvironmentDefaultsWidget';

export const environmentDefaultsFamily: CapabilityComponentFamily = {
  Widget: EnvironmentDefaultsWidget,
  Display: EnvironmentDefaultsDisplay,
};

export { EnvironmentDefaultsDisplay, EnvironmentDefaultsWidget };
