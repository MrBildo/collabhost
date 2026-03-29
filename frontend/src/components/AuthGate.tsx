import { useState, type FormEvent, type ReactNode } from 'react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { getAdminKey, setAdminKey } from '@/lib/api';

type AuthGateProps = {
  children: ReactNode;
};

export function AuthGate({ children }: AuthGateProps) {
  const [key, setKey] = useState('');
  const hasKey = !!getAdminKey();

  if (hasKey) {
    return <>{children}</>;
  }

  const handleSubmit = (event: FormEvent) => {
    event.preventDefault();
    const trimmed = key.trim();
    if (!trimmed) return;
    setAdminKey(trimmed);
    window.location.reload();
  };

  return (
    <div className="flex min-h-screen items-center justify-center bg-background">
      <div className="w-full max-w-sm rounded-lg border bg-card p-8 shadow-sm">
        <h1 className="mb-6 text-center text-2xl font-bold tracking-tight">Collabhost</h1>
        <form onSubmit={handleSubmit} className="space-y-4">
          <div className="space-y-2">
            <label htmlFor="admin-key" className="text-sm font-medium text-muted-foreground">
              Enter your admin key
            </label>
            <Input
              id="admin-key"
              type="text"
              value={key}
              onChange={(event) => setKey(event.target.value)}
              placeholder="Admin key"
              autoFocus
            />
          </div>
          <Button type="submit" className="w-full">
            Connect
          </Button>
        </form>
      </div>
    </div>
  );
}
