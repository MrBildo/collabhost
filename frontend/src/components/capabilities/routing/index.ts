import type { CapabilityComponentFamily } from '../types';

import { RoutingDisplay } from './RoutingDisplay';
import { RoutingWidget } from './RoutingWidget';

export const routingFamily: CapabilityComponentFamily = {
  Widget: RoutingWidget,
  Display: RoutingDisplay,
};

export { RoutingDisplay, RoutingWidget };
