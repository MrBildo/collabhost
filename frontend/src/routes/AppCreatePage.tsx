import { useCallback, useState } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { ArrowLeft, Loader2 } from 'lucide-react';
import { toast } from 'sonner';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import {
  GlassCard,
  GlassCardContent,
  GlassCardHeader,
  GlassCardTitle,
} from '@/components/ui/GlassCard';
import { GradientButton } from '@/components/ui/GradientButton';
import { CapabilityForm } from '@/components/capabilities/CapabilityForm';
import { useCreateApp } from '@/hooks/useApps';
import { useAppTypeList } from '@/hooks/useAppTypes';
import { BASE_DOMAIN } from '@/lib/constants';
import type { CapabilityEntry } from '@/components/capabilities/types';
import type { AppTypeListItem, CreateAppRequest } from '@/types/api';

function toSlug(value: string): string {
  return value
    .toLowerCase()
    .replace(/\s+/g, '-')
    .replace(/[^a-z0-9-]/g, '')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '');
}

function buildCapabilityEntries(appType: AppTypeListItem): CapabilityEntry[] {
  return Object.entries(appType.capabilities).map(([slug, cap]) => ({
    slug,
    category: cap.category as 'behavioral' | 'informational',
    displayName: cap.displayName,
    resolved: { ...cap.defaults },
    defaults: cap.defaults,
    hasOverrides: false,
  }));
}

export default function AppCreatePage() {
  const navigate = useNavigate();
  const createApp = useCreateApp();
  const { data: appTypes, isLoading: isAppTypesLoading } = useAppTypeList();

  const [displayName, setDisplayName] = useState('');
  const [name, setName] = useState('');
  const [selectedTypeId, setSelectedTypeId] = useState('');
  const [isSlugManuallyEdited, setIsSlugManuallyEdited] = useState(false);
  const [overrides, setOverrides] = useState<Record<string, Record<string, unknown> | null>>({});
  const [error, setError] = useState<string | null>(null);

  const selectedAppType = appTypes?.find((t) => t.id === selectedTypeId) ?? null;
  const capabilities = selectedAppType ? buildCapabilityEntries(selectedAppType) : [];

  const handleDisplayNameChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      const value = event.target.value;
      setDisplayName(value);
      if (!isSlugManuallyEdited) {
        setName(toSlug(value));
      }
    },
    [isSlugManuallyEdited],
  );

  function handleSlugChange(event: React.ChangeEvent<HTMLInputElement>) {
    setIsSlugManuallyEdited(true);
    setName(event.target.value);
  }

  function handleTypeChange(value: string | null) {
    if (value !== null) {
      setSelectedTypeId(value);
      setOverrides({});
    }
  }

  async function handleSubmit(event: React.FormEvent) {
    event.preventDefault();
    setError(null);

    if (!selectedTypeId) {
      setError('Please select an app type.');
      return;
    }

    const request: CreateAppRequest = {
      name,
      displayName,
      appTypeId: selectedTypeId,
      capabilityOverrides: Object.keys(overrides).length > 0 ? overrides : null,
    };

    try {
      const result = await createApp.mutateAsync(request);
      toast.success('App created');
      navigate(`/apps/${result.externalId}`);
    } catch (err: unknown) {
      const axiosError = err as { response?: { data?: { errorMessage?: string } } };
      const message =
        axiosError?.response?.data?.errorMessage ??
        (err instanceof Error ? err.message : String(err));
      setError(message);
      toast.error('Failed to create app');
    }
  }

  return (
    <div className="p-6">
      <Link
        to="/apps"
        className="mb-4 inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="h-4 w-4" />
        Apps
      </Link>

      <h1
        className="mb-6 text-2xl font-bold tracking-tight"
        style={{ fontFamily: "'Space Grotesk', sans-serif" }}
      >
        Add App
      </h1>

      <form onSubmit={handleSubmit} className="max-w-2xl space-y-6">
        <GlassCard>
          <GlassCardHeader>
            <GlassCardTitle>Basics</GlassCardTitle>
          </GlassCardHeader>
          <GlassCardContent className="space-y-4">
            <div className="space-y-1.5">
              <label htmlFor="displayName" className="text-sm font-medium">
                Display Name <span className="text-destructive">*</span>
              </label>
              <Input
                id="displayName"
                value={displayName}
                onChange={handleDisplayNameChange}
                placeholder="My Application"
                required
              />
            </div>

            <div className="space-y-1.5">
              <label htmlFor="name" className="text-sm font-medium">
                Slug <span className="text-destructive">*</span>
              </label>
              <Input
                id="name"
                value={name}
                onChange={handleSlugChange}
                placeholder="my-application"
                required
              />
              <p className="text-xs text-muted-foreground">
                Used in the domain: {name || 'my-app'}.{BASE_DOMAIN}
              </p>
            </div>

            <div className="space-y-1.5">
              <label className="text-sm font-medium">
                Type <span className="text-destructive">*</span>
              </label>
              {isAppTypesLoading ? (
                <div className="flex h-9 items-center">
                  <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
                </div>
              ) : (
                <Select value={selectedTypeId} onValueChange={handleTypeChange}>
                  <SelectTrigger className="w-full">
                    <SelectValue placeholder="Select an app type">
                      {selectedAppType?.displayName}
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    {(appTypes ?? []).map((type) => (
                      <SelectItem key={type.id} value={type.id}>
                        {type.displayName}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </div>
          </GlassCardContent>
        </GlassCard>

        {/* Capabilities form — shown when a type is selected */}
        {selectedAppType && capabilities.length > 0 && (
          <GlassCard>
            <GlassCardHeader>
              <GlassCardTitle>Capabilities</GlassCardTitle>
            </GlassCardHeader>
            <GlassCardContent>
              <CapabilityForm capabilities={capabilities} onOverridesChange={setOverrides} />
            </GlassCardContent>
          </GlassCard>
        )}

        {error && (
          <div className="rounded-lg border border-destructive/50 bg-destructive/10 p-3 text-sm text-destructive">
            {error}
          </div>
        )}

        <div className="flex items-center gap-3">
          <GradientButton
            type="submit"
            disabled={createApp.isPending || isAppTypesLoading || !selectedTypeId}
          >
            {createApp.isPending && <Loader2 className="mr-1 h-4 w-4 animate-spin" />}
            Create App
          </GradientButton>
          <GradientButton
            type="button"
            variant="outline"
            onClick={() => navigate('/apps')}
            disabled={createApp.isPending}
          >
            Cancel
          </GradientButton>
        </div>
      </form>
    </div>
  );
}
