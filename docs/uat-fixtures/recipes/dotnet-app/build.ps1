<#
.SYNOPSIS
  Build the dotnet-app UAT fixtures into docs/uat-fixtures/build/dotnet-app/.
.DESCRIPTION
  Variants per the per-type README:
    framework-dependent/         - normal `dotnet publish`
    self-contained/              - --self-contained + PublishSingleFile=true
    self-contained-pdb-stripped/ - self-contained + DebugType=none

  Pinned versions live in sources/UatDotnetFixture.csproj.

  Reproducibility: Deterministic=true + PathMap normalise the build; same-machine
  re-runs are byte-identical post-mtime-pin. Cross-machine reproducibility
  requires identical SDK patch versions.
#>
[CmdletBinding()]
param(
    [string]$Rid = ''
)

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $here '..\..\..\..')).Path
$srcRoot = Join-Path $here 'sources'
$outRoot = Join-Path $repoRoot 'docs\uat-fixtures\build\dotnet-app'

if (-not $Rid) {
    if ($IsLinux) { $Rid = 'linux-x64' }
    elseif ($IsMacOS) { $Rid = 'osx-x64' }
    else { $Rid = 'win-x64' }
}

if (-not (Test-Path $outRoot)) {
    New-Item -ItemType Directory -Path $outRoot -Force | Out-Null
}

function Build-Variant {
    param(
        [string]$Name,
        [string[]]$ExtraArgs
    )
    $dst = Join-Path $outRoot $Name
    if (Test-Path $dst) {
        Remove-Item -Recurse -Force $dst
    }
    New-Item -ItemType Directory -Path $dst -Force | Out-Null

    Write-Host ">>> $Name (RID=$Rid)"
    $csproj = Join-Path $srcRoot 'UatDotnetFixture.csproj'
    $args = @(
        'publish', $csproj,
        '--configuration', 'Release',
        '--output', $dst,
        '--nologo',
        '--verbosity', 'quiet'
    ) + $ExtraArgs
    & dotnet @args
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Name (exit $LASTEXITCODE)"
    }
}

Build-Variant -Name 'framework-dependent' -ExtraArgs @()

Build-Variant -Name 'self-contained' -ExtraArgs @(
    '--runtime', $Rid,
    '--self-contained', 'true',
    '-p:PublishSingleFile=true'
)

Build-Variant -Name 'self-contained-pdb-stripped' -ExtraArgs @(
    '--runtime', $Rid,
    '--self-contained', 'true',
    '-p:PublishSingleFile=true',
    '-p:DebugType=none',
    '-p:DebugSymbols=false',
    # Suppress the static-web-assets manifest so the detect-strategy sees only the
    # `single-file-binary` signal (runbook §4 K-1 corner case: `Manual` with empty
    # or single signal, no `staticwebassets.endpoints.json`).
    '-p:StaticWebAssetsEnabled=false'
)

$stamp = [datetime]::Parse('2026-01-01T00:00:00Z').ToUniversalTime()
Get-ChildItem -Recurse -Path $outRoot -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        $_.LastWriteTimeUtc = $stamp
        $_.CreationTimeUtc = $stamp
    } catch {
        # Some publish outputs (locked .exe, .pdb on Windows) may briefly resist mtime updates.
        # Ignore - mtime stability is a nice-to-have, not load-bearing for the fixture.
    }
}

Write-Host "dotnet-app fixtures built at: $outRoot"
