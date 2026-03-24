import { useQuery } from '@tanstack/react-query';
import axios from 'axios';

interface StatusResponse {
  status: string;
  version: string;
  timestamp: string;
}

function App() {
  const { data, isLoading, error } = useQuery<StatusResponse>({
    queryKey: ['status'],
    queryFn: () => axios.get('/api/v1/status').then((res) => res.data),
  });

  return (
    <div className="flex min-h-screen items-center justify-center bg-background text-foreground">
      <div className="text-center space-y-4">
        <h1 className="text-4xl font-bold tracking-tight">Collabhost</h1>
        <p className="text-muted-foreground">Self-hosted application platform</p>
        {isLoading && <p className="text-sm text-muted-foreground">Connecting...</p>}
        {error && <p className="text-sm text-destructive">API unreachable</p>}
        {data && (
          <div className="rounded-lg border bg-card p-4 text-card-foreground text-sm">
            <p>
              Status: <span className="font-medium text-green-500">{data.status}</span>
            </p>
            <p>
              Version: <span className="font-mono">{data.version}</span>
            </p>
          </div>
        )}
      </div>
    </div>
  );
}

export default App;
