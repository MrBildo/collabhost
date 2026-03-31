import type { CapabilityComponentFamily } from '../types';

import { ProcessDisplay } from './ProcessDisplay';
import { ProcessSummary } from './ProcessSummary';
import { ProcessWidget } from './ProcessWidget';

export const processFamily: CapabilityComponentFamily = {
  Widget: ProcessWidget,
  Display: ProcessDisplay,
  Summary: ProcessSummary,
};

export { ProcessDisplay, ProcessSummary, ProcessWidget };
