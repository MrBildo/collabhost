import { App } from '@/app'
import { AuthGate } from '@/chrome/AuthGate'
import { shouldRetryQuery } from '@/lib/query-retry'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import '@/styles/index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Retry transient (5xx / network) failures once; never retry a 4xx
      // settled answer (FE-QRY-04). The bare `retry: 1` retried everything.
      retry: shouldRetryQuery,
      staleTime: 5_000,
      refetchOnWindowFocus: false,
    },
  },
})

const root = document.getElementById('root')
if (!root) throw new Error('Root element not found')

createRoot(root).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AuthGate>
        <App />
      </AuthGate>
    </QueryClientProvider>
  </StrictMode>,
)
