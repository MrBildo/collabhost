import { FallbackDisplay } from './fallback/FallbackDisplay';
import { aspnetRuntimeFamily } from './aspnet-runtime';
import { autoStartFamily } from './auto-start';
import { environmentDefaultsFamily } from './environment-defaults';
import { healthCheckFamily } from './health-check';
import { nodeRuntimeFamily } from './node-runtime';
import { portInjectionFamily } from './port-injection';
import { processFamily } from './process';
import { reactRuntimeFamily } from './react-runtime';
import { restartFamily } from './restart';
import { routingFamily } from './routing';
import type { CapabilityComponentFamily, CapabilityWidgetProps } from './types';

/** Registry mapping capability slugs to their component families. */
const capabilityRegistry = new Map<string, CapabilityComponentFamily>([
  ['process', processFamily],
  ['health-check', healthCheckFamily],
  ['environment-defaults', environmentDefaultsFamily],
  ['port-injection', portInjectionFamily],
  ['routing', routingFamily],
  ['restart', restartFamily],
  ['auto-start', autoStartFamily],
  ['aspnet-runtime', aspnetRuntimeFamily],
  ['node-runtime', nodeRuntimeFamily],
  ['react-runtime', reactRuntimeFamily],
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
