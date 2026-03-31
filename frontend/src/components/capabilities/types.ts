import type React from 'react';

/** Props passed to every capability widget (edit, display, summary). */
export type CapabilityWidgetProps = {
  /** Display name for the capability, sourced from the API. */
  displayName: string;
  /** Fully merged configuration from API (type defaults + operator overrides). */
  resolved: Record<string, unknown>;
  /** Type-level default configuration (before any overrides). */
  defaults: Record<string, unknown>;
  /** Whether the operator has any overrides for this capability. */
  hasOverrides: boolean;
  /**
   * Callback when the operator changes override values.
   * Pass the partial override object, or `null` to reset to defaults.
   * Only present in edit (Widget) mode.
   */
  onChange?: (overrides: Record<string, unknown> | null) => void;
};

/** A family of components for rendering a single capability in different contexts. */
export type CapabilityComponentFamily = {
  /** Edit mode component — used in create/edit forms. */
  Widget: React.ComponentType<CapabilityWidgetProps>;
  /** Read-only display component — used in app detail views. */
  Display: React.ComponentType<CapabilityWidgetProps>;
  /** Compact summary component — used on app cards. Optional. */
  Summary?: React.ComponentType<CapabilityWidgetProps>;
};

/** A single capability entry as received from the API response. */
export type CapabilityEntry = {
  slug: string;
  category: 'behavioral' | 'informational';
  displayName: string;
  resolved: Record<string, unknown>;
  defaults: Record<string, unknown>;
  hasOverrides: boolean;
};
