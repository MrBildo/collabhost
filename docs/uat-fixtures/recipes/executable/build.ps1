<#
.SYNOPSIS
  Build the executable UAT fixtures into docs/uat-fixtures/build/executable/.
.DESCRIPTION
  Variants per the per-type README:
    single-binary/      - one Go binary at root, listens on $PORT
    multiple-binaries/  - two Go binaries at root (sorted: aaa, bbb)
    looks-like-dotnet/  - copy of dotnet-app/self-contained/

  Cross-OS: Windows produces `*.exe`; Linux produces extensionless +x binary.

  Determinism: -trimpath + -buildvcs=false + -ldflags='-s -w -buildid=' make
  the build deterministic for the same toolchain on the same machine.

  Prerequisite for `looks-like-dotnet/`: the dotnet-app recipe must have been
  run first. If the source dir is missing, looks-like-dotnet/ is skipped with
  a warning (not a fatal error - the single-binary/multiple-binaries variants
  remain fully usable).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Resolve-Path (Join-Path $here '..\..\..\..')).Path
$srcRoot = Join-Path $here 'sources'
$outRoot = Join-Path $repoRoot 'docs\uat-fixtures\build\executable'

$binExt = if ($IsWindows -or ($null -eq $IsWindows -and $env:OS -eq 'Windows_NT')) { '.exe' } else { '' }

if (-not (Test-Path $outRoot)) {
    New-Item -ItemType Directory -Path $outRoot -Force | Out-Null
}

# --- single-binary/ ---
$sb = Join-Path $outRoot 'single-binary'
if (Test-Path $sb) { Remove-Item -Recurse -Force $sb }
New-Item -ItemType Directory -Path $sb -Force | Out-Null

Push-Location $srcRoot
try {
    $binPath = Join-Path $sb "uat-executable$binExt"
    & go build -trimpath -buildvcs=false -ldflags='-s -w -buildid=' -o $binPath ./...
    if ($LASTEXITCODE -ne 0) {
        throw "go build failed (exit $LASTEXITCODE)"
    }
} finally {
    Pop-Location
}

# --- multiple-binaries/ ---
$mb = Join-Path $outRoot 'multiple-binaries'
if (Test-Path $mb) { Remove-Item -Recurse -Force $mb }
New-Item -ItemType Directory -Path $mb -Force | Out-Null

$singleBin = Join-Path $sb "uat-executable$binExt"
Copy-Item -Path $singleBin -Destination (Join-Path $mb "aaa$binExt")
Copy-Item -Path $singleBin -Destination (Join-Path $mb "bbb$binExt")

# --- looks-like-dotnet/ ---
$ld = Join-Path $outRoot 'looks-like-dotnet'
if (Test-Path $ld) { Remove-Item -Recurse -Force $ld }
New-Item -ItemType Directory -Path $ld -Force | Out-Null

$srcDn = Join-Path $repoRoot 'docs\uat-fixtures\build\dotnet-app\self-contained'
if (-not (Test-Path $srcDn)) {
    Write-Warning "dotnet-app/self-contained/ not built yet - run the dotnet-app recipe first."
    Write-Warning "looks-like-dotnet/ left empty."
} else {
    Copy-Item -Recurse -Path (Join-Path $srcDn '*') -Destination $ld
}

$stamp = [datetime]::Parse('2026-01-01T00:00:00Z').ToUniversalTime()
Get-ChildItem -Recurse -Path $outRoot -ErrorAction SilentlyContinue | ForEach-Object {
    try {
        $_.LastWriteTimeUtc = $stamp
        $_.CreationTimeUtc = $stamp
    } catch {
        # Some files briefly lock; mtime stability is best-effort.
    }
}

Write-Host "executable fixtures built at: $outRoot"
