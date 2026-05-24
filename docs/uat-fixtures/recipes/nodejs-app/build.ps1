<#
.SYNOPSIS
  Build the nodejs-app UAT fixtures into docs/uat-fixtures/build/nodejs-app/.
.DESCRIPTION
  See build.sh for strategy notes (stdlib server, express declared for probes only,
  no node_modules populated by this recipe).

  Idempotent: rerunning produces byte-identical output.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $here '..\..\..\..')).Path
$srcRoot = Join-Path $here 'sources'
$outRoot = Join-Path $repoRoot 'docs\uat-fixtures\build\nodejs-app'

$variants = @('with-start-script', 'no-start-script', 'malformed-package-json')

if (-not (Test-Path $outRoot)) {
    New-Item -ItemType Directory -Path $outRoot -Force | Out-Null
}

foreach ($v in $variants) {
    $dst = Join-Path $outRoot $v
    if (Test-Path $dst) {
        Remove-Item -Recurse -Force $dst
    }
    New-Item -ItemType Directory -Path $dst -Force | Out-Null
    Copy-Item -Recurse -Path (Join-Path $srcRoot "$v\*") -Destination $dst
}

$stamp = [datetime]::Parse('2026-01-01T00:00:00Z').ToUniversalTime()
Get-ChildItem -Recurse -Path $outRoot | ForEach-Object {
    $_.LastWriteTimeUtc = $stamp
    $_.CreationTimeUtc = $stamp
}

Write-Host "nodejs-app fixtures built at: $outRoot"
