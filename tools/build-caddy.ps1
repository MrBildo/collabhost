#requires -Version 7
<#
.SYNOPSIS
  Build a local Caddy binary that matches the shipped CI build (core +
  plugins from caddy-plugins.txt) and drop it at tools/caddy/caddy.exe.

.DESCRIPTION
  Most contributors don't need this -- the proxy defaults to Caddy's
  internal CA, which doesn't depend on any DNS plugin. Run this only if
  you're locally exercising the ACME branch (Proxy:DnsProvider set).

  Reads `caddy.version`, `xcaddy.version`, `go.version`,
  `caddy-plugins.txt`, and `caddy-replaces.txt` at the repo root. Installs the pinned xcaddy into
  GOPATH/bin. The Go toolchain in `go.version` is the one the release
  pipeline compiles Caddy with; this script selects it through GOTOOLCHAIN,
  so any Go 1.21+ on PATH downloads and uses the pinned toolchain.
  Cross-compile is left to the operator -- this script targets the host
  OS/arch.

.NOTES
  Requires Go 1.21 or later (https://go.dev/dl/) on PATH.
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

function Get-Replaces {
    $path = Join-Path $RepoRoot 'caddy-replaces.txt'
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Pin file not found: $path"
    }
    Get-Content -LiteralPath $path |
        Where-Object { $_.Trim() -and -not $_.TrimStart().StartsWith('#') } |
        ForEach-Object {
            $parts = $_.Trim() -split '\s+'
            if ($parts.Count -ne 2) {
                throw "Malformed replace line: $_"
            }
            [PSCustomObject]@{
                Module  = $parts[0]
                Version = $parts[1]
            }
        }
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

# Check the build metadata a Caddy binary records (`go version -m`) against the
# pins. Same rules as tools/verify-caddy-build.sh, which the workflows and
# build-caddy.sh run:
#   - the toolchain must be the one in go.version;
#   - every caddy-replaces.txt override must be in the binary and still needed.
#     A Go `replace` with a version replaces every version of the module, so a
#     stale override is otherwise invisible: once Caddy requires the pinned
#     version the line does nothing, and once Caddy requires a newer one the
#     line silently holds the module back. The required version (the `dep`
#     line) must therefore be older than the override.
# Versions compare as major.minor.patch; pre-release suffixes are ignored.
function Assert-CaddyBuildInfo {
    param(
        [Parameter(Mandatory)] [string] $BinaryPath,
        [Parameter(Mandatory)] [string] $GoVersion,
        [object[]] $Replaces = @()
    )

    $toCore = {
        param([string] $v)
        [version] (($v -replace '^v', '') -split '[-+]')[0]
    }

    $buildInfo = @(& go version -m $BinaryPath)
    if ($LASTEXITCODE -ne 0 -or $buildInfo.Count -eq 0) {
        throw "go version -m could not read build information from $BinaryPath"
    }
    $builtWith = ($buildInfo[0] -split '\s+')[-1]
    if ($builtWith -ne "go$GoVersion") {
        throw "Built Caddy reports toolchain '$builtWith', expected 'go$GoVersion' (go.version)"
    }
    Write-Host "  ok: compiled with $builtWith" -ForegroundColor Green

    foreach ($r in $Replaces) {
        $required = $null
        $landed = $false
        foreach ($line in $buildInfo) {
            $cols = $line.Trim() -split '\s+'
            if ($cols.Count -lt 3 -or $cols[1] -ne $r.Module) { continue }
            if ($cols[0] -eq 'dep') { $required = $cols[2] }
            if ($cols[0] -eq '=>' -and $cols[2] -eq $r.Version) { $landed = $true }
        }
        if (-not $landed) {
            throw "MISSING override: $($r.Module) $($r.Version) (caddy-replaces.txt) is not in the binary"
        }
        if ($required -and (& $toCore $required) -ge (& $toCore $r.Version)) {
            throw "STALE override: $($r.Module) $($r.Version) (caddy-replaces.txt). The build already requires $required; delete this line."
        }
        Write-Host "  ok: $($r.Module) $required => $($r.Version)" -ForegroundColor Green
    }
}

if (-not (Get-Command go -ErrorAction SilentlyContinue)) {
    throw "Go is not installed or not on PATH. See https://go.dev/dl/."
}

$caddyVersion = Get-PinFile 'caddy.version'
$xcaddyVersion = Get-PinFile 'xcaddy.version'
$goVersion = Get-PinFile 'go.version'
$plugins = @(Get-Plugins)
$replaces = @(Get-Replaces)

Write-Host "Caddy core:   v$caddyVersion"
Write-Host "xcaddy:       v$xcaddyVersion"
Write-Host "Go toolchain: go$goVersion"
Write-Host "Plugins:"
foreach ($p in $plugins) {
    Write-Host "  $($p.Module)@$($p.Version)" -ForegroundColor DarkCyan
}
Write-Host "Module overrides:"
foreach ($r in $replaces) {
    Write-Host "  $($r.Module) => $($r.Version)" -ForegroundColor DarkCyan
}

# Every go and xcaddy invocation below runs on the pinned toolchain. A bare
# version (no +auto suffix) also stops a module from switching to a newer one,
# so a dependency that needs a later Go fails the build instead of silently
# changing what gets compiled.
$previousToolchain = $env:GOTOOLCHAIN
$env:GOTOOLCHAIN = "go$goVersion"
try {
    $gopath = (& go env GOPATH).Trim()
    $xcaddyExe = Join-Path $gopath 'bin\xcaddy.exe'

    # Always install the pinned xcaddy: an older one left in GOPATH/bin by an
    # earlier pin would otherwise keep being used after a bump.
    Write-Host "Installing xcaddy v$xcaddyVersion ..."
    & go install "github.com/caddyserver/xcaddy/cmd/xcaddy@v$xcaddyVersion"
    if ($LASTEXITCODE -ne 0) {
        throw "go install xcaddy failed (exit $LASTEXITCODE)"
    }

    $withArgs = foreach ($p in $plugins) { @('--with', "$($p.Module)@$($p.Version)") }
    $replaceArgs = @(foreach ($r in $replaces) { @('--replace', "$($r.Module)=$($r.Module)@$($r.Version)") })

    $outputDir = Split-Path -Parent $OutputPath
    if (-not (Test-Path -LiteralPath $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }

    Write-Host ""
    Write-Host "Building Caddy v$caddyVersion -> $OutputPath"
    & $xcaddyExe build "v$caddyVersion" @withArgs @replaceArgs --output $OutputPath
    if ($LASTEXITCODE -ne 0) {
        throw "xcaddy build failed (exit $LASTEXITCODE)"
    }

    Assert-CaddyBuildInfo -BinaryPath $OutputPath -GoVersion $goVersion -Replaces $replaces
}
finally {
    $env:GOTOOLCHAIN = $previousToolchain
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
