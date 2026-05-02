#requires -Version 7
<#
.SYNOPSIS
  Build a local Caddy binary that matches the shipped CI build (core +
  plugins from caddy-plugins.txt) and drop it at tools/caddy/caddy.exe.

.DESCRIPTION
  Most contributors don't need this -- the proxy defaults to Caddy's
  internal CA, which doesn't depend on any DNS plugin. Run this only if
  you're locally exercising the ACME branch (Proxy:DnsProvider set).

  Reads `caddy.version`, `xcaddy.version`, and `caddy-plugins.txt` at the
  repo root. Installs xcaddy into GOPATH/bin if missing. Cross-compile
  is left to the operator -- this script targets the host OS/arch.

.NOTES
  Requires Go (https://go.dev/dl/) on PATH.
#>

[CmdletBinding()]
param(
    [string] $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string] $OutputPath = (Join-Path $PSScriptRoot 'caddy\caddy.exe')
)

$ErrorActionPreference = 'Stop'

function Get-PinFile([string] $name) {
    $path = Join-Path $RepoRoot $name
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Pin file not found: $path"
    }
    (Get-Content -LiteralPath $path -Raw).Trim()
}

function Get-Plugins {
    $path = Join-Path $RepoRoot 'caddy-plugins.txt'
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Pin file not found: $path"
    }
    Get-Content -LiteralPath $path |
        Where-Object { $_ -and -not $_.TrimStart().StartsWith('#') } |
        ForEach-Object {
            $parts = $_.Trim() -split '\s+'
            if ($parts.Count -lt 2) {
                throw "Malformed plugin line: $_"
            }
            [PSCustomObject]@{
                Module    = $parts[0]
                Version   = $parts[1]
                CaddyId   = if ($parts.Count -ge 3) { $parts[2] } else { $null }
            }
        }
}

if (-not (Get-Command go -ErrorAction SilentlyContinue)) {
    throw "Go is not installed or not on PATH. See https://go.dev/dl/."
}

$caddyVersion = Get-PinFile 'caddy.version'
$xcaddyVersion = Get-PinFile 'xcaddy.version'
$plugins = @(Get-Plugins)

Write-Host "Caddy core:   v$caddyVersion"
Write-Host "xcaddy:       v$xcaddyVersion"
Write-Host "Plugins:"
foreach ($p in $plugins) {
    Write-Host "  $($p.Module)@$($p.Version)" -ForegroundColor DarkCyan
}

$gopath = (& go env GOPATH).Trim()
$xcaddyExe = Join-Path $gopath 'bin\xcaddy.exe'

if (-not (Test-Path -LiteralPath $xcaddyExe)) {
    Write-Host "Installing xcaddy v$xcaddyVersion ..."
    & go install "github.com/caddyserver/xcaddy/cmd/xcaddy@v$xcaddyVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "go install xcaddy failed (exit $LASTEXITCODE)"
    }
}

$withArgs = foreach ($p in $plugins) { @('--with', "$($p.Module)@$($p.Version)") }

$outputDir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

Write-Host ""
Write-Host "Building Caddy v$caddyVersion -> $OutputPath"
& $xcaddyExe build "v$caddyVersion" @withArgs --output $OutputPath
if ($LASTEXITCODE -ne 0) {
    throw "xcaddy build failed (exit $LASTEXITCODE)"
}

Write-Host ""
Write-Host "Asserting baked-in plugin modules ..."
$modules = & $OutputPath list-modules
foreach ($p in $plugins) {
    if (-not $p.CaddyId) { continue }
    if ($modules -notcontains $p.CaddyId -and ($modules | Where-Object { $_.StartsWith($p.CaddyId) }).Count -eq 0) {
        throw "Plugin missing from built binary: $($p.CaddyId) (module: $($p.Module))"
    }
    Write-Host "  ok: $($p.CaddyId)" -ForegroundColor Green
}

Write-Host ""
Write-Host "Built Caddy: $OutputPath"
