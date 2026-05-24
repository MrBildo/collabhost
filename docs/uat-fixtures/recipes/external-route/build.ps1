<#
.SYNOPSIS
  Build the external-route UAT fixture into docs/uat-fixtures/build/external-route/.
.DESCRIPTION
  Writes the side-process working directory. The operator launches the side-process
  explicitly before registration:

    cd docs\uat-fixtures\build\external-route\localhost-http
    python -m http.server 11235

  `python -m http.server` serves files by literal name; the `health` file (no
  extension) returns 200 for GET /health.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $here '..\..\..\..')).Path
$srcRoot = Join-Path $here 'sources'
$outRoot = Join-Path $repoRoot 'docs\uat-fixtures\build\external-route'

if (-not (Test-Path $outRoot)) {
    New-Item -ItemType Directory -Path $outRoot -Force | Out-Null
}

$dst = Join-Path $outRoot 'localhost-http'
if (Test-Path $dst) {
    Remove-Item -Recurse -Force $dst
}
New-Item -ItemType Directory -Path $dst -Force | Out-Null

Copy-Item -Path (Join-Path $srcRoot 'localhost-http\index.html') -Destination (Join-Path $dst 'index.html')
Copy-Item -Path (Join-Path $srcRoot 'localhost-http\health') -Destination (Join-Path $dst 'health')

$stamp = [datetime]::Parse('2026-01-01T00:00:00Z').ToUniversalTime()
Get-ChildItem -Recurse -Path $outRoot | ForEach-Object {
    $_.LastWriteTimeUtc = $stamp
    $_.CreationTimeUtc = $stamp
}

Write-Host "external-route fixture built at: $dst"
Write-Host "ready to launch with: cd '$dst'; python -m http.server 11235"
