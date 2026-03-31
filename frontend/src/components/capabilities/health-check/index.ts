import type { CapabilityComponentFamily } from '../types';

import { HealthCheckDisplay } from './HealthCheckDisplay';
import { HealthCheckWidget } from './HealthCheckWidget';

export const healthCheckFamily: CapabilityComponentFamily = {
  Widget: HealthCheckWidget,
  Display: HealthCheckDisplay,
};

export { HealthCheckDisplay, HealthCheckWidget };
