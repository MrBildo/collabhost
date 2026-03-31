import { useCallback, useMemo, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { toast } from 'sonner';
import { ArrowLeft, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { getCapabilityComponents } from '@/components/capabilities/registry';
import type { CapabilityWidgetProps } from '@/components/capabilities/types';
import { useCreateAppType } from '@/hooks/useAppTypes';
import { useCapabilityCatalog } from '@/hooks/useCapabilities';
import type { CapabilityCatalogItem, CreateAppTypeRequest } from '@/types/api';

type CapabilityDefaults = Record<string, Record<string, unknown>>;

export default function AppTypeCreatePage() {
  const navigate = useNavigate();
  const createAppType = useCreateAppType();
  const { data: capabilities, isLoading: isCapabilitiesLoading } = useCapabilityCatalog();

  const [name, setName] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [description, setDescription] = useState('');
  const [selectedSlugs, setSelectedSlugs] = useState<Set<string>>(new Set());
  const [capabilityDefaults, setCapabilityDefaults] = useState<CapabilityDefaults>({});

  const handleToggleCapability = useCallback((slug: string, checked: boolean) => {
    setSelectedSlugs((previous) => {
      const next = new Set(previous);
      if (checked) {
        next.add(slug);
      } else {
        next.delete(slug);
        setCapabilityDefaults((previousDefaults) => {
          const updated = { ...previousDefaults };
          delete updated[slug];
          return updated;
        });
      }
      return next;
    });
  }, []);

  const handleCapabilityDefaultsChange = useCallback(
    (slug: string, overrides: Record<string, unknown> | null) => {
      setCapabilityDefaults((previous) => {
        const next = { ...previous };
        if (overrides === null) {
          delete next[slug];
        } else {
          next[slug] = overrides;
        }
        return next;
      });
    },
    [],
  );

  const handleSubmit = useCallback(
    (event: React.FormEvent) => {
      event.preventDefault();

      const capabilitiesPayload: Record<string, Record<string, unknown>> = {};
      for (const slug of selectedSlugs) {
        capabilitiesPayload[slug] = capabilityDefaults[slug] ?? {};
      }

      const request: CreateAppTypeRequest = {
        name,
        displayName,
        description: description || null,
        capabilities: Object.keys(capabilitiesPayload).length > 0 ? capabilitiesPayload : null,
      };

      createAppType.mutate(request, {
        onSuccess: (response) => {
          toast.success('App type created');
          navigate(`/app-types/${response.externalId}`);
        },
        onError: () => toast.error('Failed to create app type'),
      });
    },
    [name, displayName, description, selectedSlugs, capabilityDefaults, createAppType, navigate],
  );

  const { behavioral, informational } = useMemo(() => {
    const behavioralItems: CapabilityCatalogItem[] = [];
    const informationalItems: CapabilityCatalogItem[] = [];

    for (const capability of capabilities ?? []) {
      if (capability.category === 'behavioral') {
        behavioralItems.push(capability);
      } else {
        informationalItems.push(capability);
      }
    }

    return { behavioral: behavioralItems, informational: informationalItems };
  }, [capabilities]);

  return (
    <div className="p-6">
      <Link
        to="/app-types"
        className="mb-4 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" />
        App Types
      </Link>

      <h1
        className="mb-6 mt-4 text-2xl font-bold tracking-tight"
        style={{ fontFamily: "'Space Grotesk', sans-serif" }}
      >
        New App Type
      </h1>

      <form onSubmit={handleSubmit} className="max-w-2xl space-y-6">
        <div className="space-y-1.5">
          <label htmlFor="name" className="text-sm font-medium">
            Name (slug)
          </label>
          <Input
            id="name"
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="e.g. dotnet-worker"
            required
          />
          <p className="text-xs text-muted-foreground">
            Lowercase identifier used in routing and config. Cannot be changed later.
          </p>
        </div>

        <div className="space-y-1.5">
          <label htmlFor="displayName" className="text-sm font-medium">
            Display Name
          </label>
          <Input
            id="displayName"
            value={displayName}
            onChange={(event) => setDisplayName(event.target.value)}
            placeholder="e.g. .NET Worker"
            required
          />
        </div>

        <div className="space-y-1.5">
          <label htmlFor="description" className="text-sm font-medium">
            Description
          </label>
          <Input
            id="description"
            value={description}
            onChange={(event) => setDescription(event.target.value)}
            placeholder="Optional description"
          />
        </div>

        {isCapabilitiesLoading ? (
          <div className="py-4 text-sm text-muted-foreground">Loading capabilities...</div>
        ) : (
          <div className="space-y-6">
            <h2
              className="text-lg font-semibold"
              style={{ fontFamily: "'Space Grotesk', sans-serif" }}
            >
              Capabilities
            </h2>

            {behavioral.length > 0 && (
              <CapabilityGroup
                title="Behavioral"
                capabilities={behavioral}
                selectedSlugs={selectedSlugs}
                capabilityDefaults={capabilityDefaults}
                onToggle={handleToggleCapability}
                onDefaultsChange={handleCapabilityDefaultsChange}
              />
            )}

            {informational.length > 0 && (
              <CapabilityGroup
                title="Informational"
                capabilities={informational}
                selectedSlugs={selectedSlugs}
                capabilityDefaults={capabilityDefaults}
                onToggle={handleToggleCapability}
                onDefaultsChange={handleCapabilityDefaultsChange}
              />
            )}
          </div>
        )}

        <div className="flex items-center gap-3 pt-4">
          <Button type="submit" disabled={createAppType.isPending}>
            {createAppType.isPending && <Loader2 className="mr-1 h-4 w-4 animate-spin" />}
            Create App Type
          </Button>
          <Button variant="outline" type="button" render={<Link to="/app-types" />}>
            Cancel
          </Button>
        </div>

        {createAppType.isError && (
          <div className="rounded-lg border border-destructive/50 bg-destructive/10 p-3 text-sm text-destructive">
            Failed to create app type. {createAppType.error.message}
          </div>
        )}
      </form>
    </div>
  );
}

type CapabilityGroupProps = {
  title: string;
  capabilities: CapabilityCatalogItem[];
  selectedSlugs: Set<string>;
  capabilityDefaults: CapabilityDefaults;
  onToggle: (slug: string, checked: boolean) => void;
  onDefaultsChange: (slug: string, overrides: Record<string, unknown> | null) => void;
};

function CapabilityGroup({
  title,
  capabilities,
  selectedSlugs,
  capabilityDefaults,
  onToggle,
  onDefaultsChange,
}: CapabilityGroupProps) {
  return (
    <div className="space-y-3">
      <h3 className="text-sm font-medium text-muted-foreground">{title}</h3>
      {capabilities.map((capability) => {
        const isSelected = selectedSlugs.has(capability.slug);

        return (
          <div key={capability.slug} className="space-y-3 rounded-lg border p-4">
            <label className="flex cursor-pointer items-start gap-3">
              <input
                type="checkbox"
                checked={isSelected}
                onChange={(event) => onToggle(capability.slug, event.target.checked)}
                className="mt-0.5 rounded border-input"
              />
              <div className="flex-1">
                <span className="text-sm font-medium">{capability.displayName}</span>
                {capability.description && (
                  <p className="mt-0.5 text-xs text-muted-foreground">{capability.description}</p>
                )}
              </div>
            </label>

            {isSelected && (
              <CapabilityDefaultsEditor
                slug={capability.slug}
                displayName={capability.displayName}
                currentDefaults={capabilityDefaults[capability.slug] ?? {}}
                onDefaultsChange={onDefaultsChange}
              />
            )}
          </div>
        );
      })}
    </div>
  );
}

type CapabilityDefaultsEditorProps = {
  slug: string;
  displayName: string;
  currentDefaults: Record<string, unknown>;
  onDefaultsChange: (slug: string, overrides: Record<string, unknown> | null) => void;
};

function CapabilityDefaultsEditor({
  slug,
  displayName,
  currentDefaults,
  onDefaultsChange,
}: CapabilityDefaultsEditorProps) {
  const family = getCapabilityComponents(slug);

  const widgetProps: CapabilityWidgetProps = {
    displayName,
    resolved: currentDefaults,
    defaults: {},
    hasOverrides: false,
    onChange: (overrides) => onDefaultsChange(slug, overrides),
  };

  return (
    <div className="ml-7 rounded-md border border-dashed p-3">
      <p className="mb-3 text-xs font-medium text-muted-foreground">Default Configuration</p>
      <family.Widget {...widgetProps} />
    </div>
  );
}
