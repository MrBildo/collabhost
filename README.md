# Collabhost

A self-hosted application platform for managing local services, workers, and scheduled jobs.

## Prerequisites

### Caddy (Reverse Proxy)

Collabhost uses [Caddy](https://caddyserver.com/) as its reverse proxy. The proxy binary is optional for development -- if not found, proxy features are disabled gracefully.

**Option A: Local binary (recommended for development)**

Download the Caddy binary to `tools/caddy/` and set the absolute path in `appsettings.Development.json`:

```json
{
  "Proxy": {
    "BinaryPath": "<absolute-path-to-repo>/tools/caddy/caddy.exe"
  }
}
```

**Option B: Install globally**

```powershell
winget install CaddyServer.Caddy
```

When installed globally, the default `BinaryPath` of `"caddy"` in `appsettings.json` will resolve it from PATH.

## Quick Start

```powershell
# Start with Aspire (recommended)
dotnet run --project backend/Collabhost.AppHost

# Frontend only
cd frontend && npm run dev
```

## License

MIT
