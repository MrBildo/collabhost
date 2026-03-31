import type { CapabilityComponentFamily } from '../types';

import { RestartDisplay } from './RestartDisplay';
import { RestartWidget } from './RestartWidget';

export const restartFamily: CapabilityComponentFamily = {
  Widget: RestartWidget,
  Display: RestartDisplay,
};

export { RestartDisplay, RestartWidget };
