import * as React from 'react';
import { Loader2 } from 'lucide-react';
import { toast } from 'sonner';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog';
import { Input } from '@/components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import { useCreateApp } from '@/hooks/useApps';
import { APP_TYPES, RESTART_POLICIES } from '@/lib/constants';
import type { CreateAppRequest } from '@/types/api';

type CreateAppDialogProps = {
  isOpen: boolean;
  onOpenChange: (open: boolean) => void;
};

function toSlug(value: string): string {
  return value
    .toLowerCase()
    .replace(/\s+/g, '-')
    .replace(/[^a-z0-9-]/g, '')
    .replace(/-+/g, '-')
    .replace(/^-|-$/g, '');
}

type FormState = {
  displayName: string;
  name: string;
  appTypeId: string;
  installDirectory: string;
  commandLine: string;
  arguments: string;
  workingDirectory: string;
  restartPolicyId: string;
  healthEndpoint: string;
  updateCommand: string;
  updateTimeoutSeconds: string;
  autoStart: boolean;
};

const STATIC_SITE_TYPE_ID = APP_TYPES.find((t) => t.name === 'StaticSite')!.id;

const INITIAL_FORM_STATE: FormState = {
  displayName: '',
  name: '',
  appTypeId: APP_TYPES[0].id,
  installDirectory: '',
  commandLine: '',
  arguments: '',
  workingDirectory: '',
  restartPolicyId: RESTART_POLICIES[0].id,
  healthEndpoint: '',
  updateCommand: '',
  updateTimeoutSeconds: '',
  autoStart: false,
};

export function CreateAppDialog({ isOpen, onOpenChange }: CreateAppDialogProps) {
  const createApp = useCreateApp();
  const [form, setForm] = React.useState(INITIAL_FORM_STATE);
  const [isSlugManuallyEdited, setIsSlugManuallyEdited] = React.useState(false);
  const [error, setError] = React.useState<string | null>(null);

  const isStaticSite = form.appTypeId === STATIC_SITE_TYPE_ID;

  const handleReset = React.useCallback(() => {
    setForm(INITIAL_FORM_STATE);
    setIsSlugManuallyEdited(false);
    setError(null);
  }, []);

  const handleOpenChange = React.useCallback(
    (open: boolean) => {
      if (!open) {
        handleReset();
      }
      onOpenChange(open);
    },
    [onOpenChange, handleReset],
  );

  const handleDisplayNameChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const displayName = event.target.value;
    setForm((previous) => ({
      ...previous,
      displayName,
      name: isSlugManuallyEdited ? previous.name : toSlug(displayName),
    }));
  };

  const handleSlugChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setIsSlugManuallyEdited(true);
    setForm((previous) => ({ ...previous, name: event.target.value }));
  };

  const handleInputChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    const { name, value } = event.target;
    setForm((previous) => ({ ...previous, [name]: value }));
  };

  const handleCheckboxChange = (event: React.ChangeEvent<HTMLInputElement>) => {
    setForm((previous) => ({ ...previous, autoStart: event.target.checked }));
  };

  const handleSubmit = async (event: React.FormEvent) => {
    event.preventDefault();
    setError(null);

    const request: CreateAppRequest = {
      name: form.name,
      displayName: form.displayName,
      appTypeId: form.appTypeId,
      installDirectory: form.installDirectory,
      commandLine: form.commandLine,
      arguments: form.arguments || null,
      workingDirectory: form.workingDirectory || null,
      restartPolicyId: form.restartPolicyId,
      healthEndpoint: form.healthEndpoint || null,
      updateCommand: form.updateCommand || null,
      updateTimeoutSeconds: form.updateTimeoutSeconds ? Number(form.updateTimeoutSeconds) : null,
      autoStart: form.autoStart,
    };

    try {
      await createApp.mutateAsync(request);
      toast.success('App created');
      handleOpenChange(false);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setError(message);
      toast.error('Failed to create app');
    }
  };

  return (
    <Dialog open={isOpen} onOpenChange={handleOpenChange}>
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Add App</DialogTitle>
          <DialogDescription>
            Register a new application to manage with Collabhost.
          </DialogDescription>
        </DialogHeader>

        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-1.5">
            <label htmlFor="displayName" className="text-sm font-medium">
              Display Name <span className="text-destructive">*</span>
            </label>
            <Input
              id="displayName"
              name="displayName"
              value={form.displayName}
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
              name="name"
              value={form.name}
              onChange={handleSlugChange}
              placeholder="my-application"
              required
            />
            <p className="text-xs text-muted-foreground">
              Used in the domain: {form.name || 'my-app'}.collab.internal
            </p>
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div className="space-y-1.5">
              <label className="text-sm font-medium">
                Type <span className="text-destructive">*</span>
              </label>
              <Select
                value={form.appTypeId}
                onValueChange={(value) => {
                  if (value !== null) {
                    setForm((previous) => ({ ...previous, appTypeId: value }));
                  }
                }}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {APP_TYPES.map((type) => (
                    <SelectItem key={type.id} value={type.id}>
                      {type.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="space-y-1.5">
              <label className="text-sm font-medium">
                Restart Policy <span className="text-destructive">*</span>
              </label>
              <Select
                value={form.restartPolicyId}
                onValueChange={(value) => {
                  if (value !== null) {
                    setForm((previous) => ({ ...previous, restartPolicyId: value }));
                  }
                }}
              >
                <SelectTrigger className="w-full">
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {RESTART_POLICIES.map((policy) => (
                    <SelectItem key={policy.id} value={policy.id}>
                      {policy.name}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="space-y-1.5">
            <label htmlFor="installDirectory" className="text-sm font-medium">
              Install Directory <span className="text-destructive">*</span>
            </label>
            <Input
              id="installDirectory"
              name="installDirectory"
              value={form.installDirectory}
              onChange={handleInputChange}
              placeholder="C:\Apps\my-application"
              required
            />
          </div>

          {!isStaticSite && (
            <div className="space-y-1.5">
              <label htmlFor="commandLine" className="text-sm font-medium">
                Command Line <span className="text-destructive">*</span>
              </label>
              <Input
                id="commandLine"
                name="commandLine"
                value={form.commandLine}
                onChange={handleInputChange}
                placeholder="dotnet run"
                required={!isStaticSite}
              />
            </div>
          )}

          <div className="space-y-1.5">
            <label htmlFor="arguments" className="text-sm font-medium">
              Arguments
            </label>
            <Input
              id="arguments"
              name="arguments"
              value={form.arguments}
              onChange={handleInputChange}
              placeholder="--urls http://localhost:5000"
            />
          </div>

          <div className="space-y-1.5">
            <label htmlFor="workingDirectory" className="text-sm font-medium">
              Working Directory
            </label>
            <Input
              id="workingDirectory"
              name="workingDirectory"
              value={form.workingDirectory}
              onChange={handleInputChange}
              placeholder="Leave empty to use install directory"
            />
          </div>

          <div className="space-y-1.5">
            <label htmlFor="healthEndpoint" className="text-sm font-medium">
              Health Endpoint
            </label>
            <Input
              id="healthEndpoint"
              name="healthEndpoint"
              value={form.healthEndpoint}
              onChange={handleInputChange}
              placeholder="/health"
            />
          </div>

          <div className="space-y-1.5">
            <label htmlFor="updateCommand" className="text-sm font-medium">
              Update Command
            </label>
            <Input
              id="updateCommand"
              name="updateCommand"
              value={form.updateCommand}
              onChange={handleInputChange}
              placeholder="git pull && dotnet build"
            />
          </div>

          <div className="space-y-1.5">
            <label htmlFor="updateTimeoutSeconds" className="text-sm font-medium">
              Update Timeout (seconds)
            </label>
            <Input
              id="updateTimeoutSeconds"
              name="updateTimeoutSeconds"
              type="number"
              value={form.updateTimeoutSeconds}
              onChange={handleInputChange}
              placeholder="300"
              min={1}
            />
          </div>

          <div className="flex items-center gap-2">
            <input
              id="autoStart"
              type="checkbox"
              checked={form.autoStart}
              onChange={handleCheckboxChange}
              className="h-4 w-4 rounded border-input accent-primary"
            />
            <label htmlFor="autoStart" className="text-sm font-medium">
              Auto Start
            </label>
          </div>

          {error && (
            <div className="rounded-lg border border-destructive/50 bg-destructive/10 p-3 text-sm text-destructive">
              {error}
            </div>
          )}

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => handleOpenChange(false)}
              disabled={createApp.isPending}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={createApp.isPending}>
              {createApp.isPending && <Loader2 className="h-4 w-4 animate-spin" />}
              Create
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
