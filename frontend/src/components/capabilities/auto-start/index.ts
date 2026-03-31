import type { CapabilityComponentFamily } from '../types';

import { AutoStartDisplay } from './AutoStartDisplay';
import { AutoStartWidget } from './AutoStartWidget';

export const autoStartFamily: CapabilityComponentFamily = {
  Widget: AutoStartWidget,
  Display: AutoStartDisplay,
};

export { AutoStartDisplay, AutoStartWidget };
