import { App } from '@/app'
import { AuthGate } from '@/chrome/AuthGate'
import { shouldRetryQuery } from '@/lib/query-retry'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
// The IBM Plex faces ship inside the bundle, so loading the dashboard makes no
// request to a third-party font host and works offline. Same weights as the
// design system uses (400 to 700, upright only).
import '@fontsource/ibm-plex-mono/400.css'
import '@fontsource/ibm-plex-mono/500.css'
import '@fontsource/ibm-plex-mono/600.css'
import '@fontsource/ibm-plex-mono/700.css'
import '@fontsource/ibm-plex-sans/400.css'
import '@fontsource/ibm-plex-sans/500.css'
import '@fontsource/ibm-plex-sans/600.css'
import '@fontsource/ibm-plex-sans/700.css'
import '@/styles/index.css'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Retry transient (5xx / network) failures once; never retry a 4xx
      // settled answer. The bare `retry: 1` retried everything.
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
