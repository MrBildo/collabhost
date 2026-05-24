<#
.SYNOPSIS
  Build the static-site UAT fixtures into docs/uat-fixtures/build/static-site/.
.DESCRIPTION
  Idempotent: rerunning produces byte-identical output.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $here '..\..\..\..')).Path
$srcRoot = Join-Path $here 'sources'
$outRoot = Join-Path $repoRoot 'docs\uat-fixtures\build\static-site'

$variants = @('basic', 'with-config-json', 'spa-bundle')

if (-not (Test-Path $outRoot)) {
    New-Item -ItemType Directory -Path $outRoot -Force | Out-Null
}

foreach ($v in $variants) {
    $dst = Join-Path $outRoot $v
    if (Test-Path $dst) {
        Remove-Item -Recurse -Force $dst
    }
    New-Item -ItemType Directory -Path $dst -Force | Out-Null
}

# basic
Copy-Item -Recurse -Path (Join-Path $srcRoot 'basic\*') -Destination (Join-Path $outRoot 'basic')

# with-config-json: basic + config.json
Copy-Item -Recurse -Path (Join-Path $srcRoot 'basic\*') -Destination (Join-Path $outRoot 'with-config-json')
Copy-Item -Path (Join-Path $srcRoot 'with-config-json\config.json') -Destination (Join-Path $outRoot 'with-config-json\config.json')

# spa-bundle
Copy-Item -Recurse -Path (Join-Path $srcRoot 'spa-bundle\*') -Destination (Join-Path $outRoot 'spa-bundle')

# Pin mtimes for stable archive hashes (no impact on per-file SHA).
$stamp = [datetime]::Parse('2026-01-01T00:00:00Z').ToUniversalTime()
Get-ChildItem -Recurse -Path $outRoot | ForEach-Object {
    $_.LastWriteTimeUtc = $stamp
    $_.CreationTimeUtc = $stamp
}

Write-Host "static-site fixtures built at: $outRoot"
