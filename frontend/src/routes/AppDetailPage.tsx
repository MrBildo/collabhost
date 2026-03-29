import { useParams } from 'react-router-dom';

export function AppDetailPage() {
  const { id } = useParams<{ id: string }>();

  return (
    <div className="p-6">
      <h1 className="text-2xl font-bold tracking-tight">App Detail</h1>
      <p className="mt-2 text-muted-foreground">
        Viewing app <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-sm">{id}</code>
      </p>
    </div>
  );
}
