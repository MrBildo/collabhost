import type { CapabilityComponentFamily } from '../types';

import { PortInjectionDisplay } from './PortInjectionDisplay';
import { PortInjectionWidget } from './PortInjectionWidget';

export const portInjectionFamily: CapabilityComponentFamily = {
  Widget: PortInjectionWidget,
  Display: PortInjectionDisplay,
};

export { PortInjectionDisplay, PortInjectionWidget };
