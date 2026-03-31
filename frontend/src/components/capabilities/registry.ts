import { FallbackDisplay } from './fallback/FallbackDisplay';
import { environmentDefaultsFamily } from './environment-defaults';
import { healthCheckFamily } from './health-check';
import { processFamily } from './process';
import type { CapabilityComponentFamily, CapabilityWidgetProps } from './types';

/** Registry mapping capability slugs to their component families. */
const capabilityRegistry = new Map<string, CapabilityComponentFamily>([
  ['process', processFamily],
  ['health-check', healthCheckFamily],
  ['environment-defaults', environmentDefaultsFamily],
]);

/**
 * Creates a fallback component family that renders raw JSON for the given slug.
 * Used when the registry does not have a widget for a capability.
 */
function createFallbackFamily(slug: string): CapabilityComponentFamily {
  function FallbackWidget(props: CapabilityWidgetProps) {
    return FallbackDisplay({ ...props, slug });
  }
  FallbackWidget.displayName = `FallbackWidget(${slug})`;

  function FallbackDisplayWrapper(props: CapabilityWidgetProps) {
    return FallbackDisplay({ ...props, slug });
  }
  FallbackDisplayWrapper.displayName = `FallbackDisplay(${slug})`;

  return {
    Widget: FallbackWidget,
    Display: FallbackDisplayWrapper,
  };
}

/**
 * Returns the component family for a capability slug.
 * If no registered widget exists, returns a fallback that renders raw JSON.
 */
function getCapabilityComponents(slug: string): CapabilityComponentFamily {
  return capabilityRegistry.get(slug) ?? createFallbackFamily(slug);
}

export { capabilityRegistry, getCapabilityComponents };
