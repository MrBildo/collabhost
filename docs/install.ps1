<#
.SYNOPSIS
    Collabhost install script (Windows).

.DESCRIPTION
    Downloads the latest (or pinned) Collabhost release archive, verifies its
    SHA-256 against the release's checksums.txt, extracts to
    $HOME\.collabhost\bin, and preserves existing appsettings.json and data/
    on re-run (upgrade-safe).

.EXAMPLE
    iwr -UseBasicParsing https://mrbildo.github.io/collabhost/install.ps1 | iex

.EXAMPLE
    .\install.ps1 -Version v0.1.0

.EXAMPLE
    # Pin via environment variable
    $env:COLLABHOST_VERSION = 'v0.1.0'; .\install.ps1
#>

# Write-Host is intentional here -- this script is invoked via
# `iwr ... | iex` and must emit progress lines to the host console, not down
# the pipeline (which would be consumed by iex).
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '')]
[CmdletBinding()]
param
(
    [string]$Version,
    [string]$InstallPath = (Join-Path $HOME '.collabhost\bin'),
    [switch]$SkipPath,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'
# Speeds up Invoke-WebRequest by avoiding Internet Explorer engine probe.
$ProgressPreference = 'SilentlyContinue'

if ($Help)
{
    Write-Host @'
Collabhost installer (Windows)

Usage: install.ps1 [options]

Options:
  -Version vX.Y.Z       Pin to a specific release tag (default: latest)
  -InstallPath PATH     Install to PATH (default: $HOME\.collabhost\bin)
  -SkipPath             Do not modify User PATH environment variable
  -Help                 Print this message and exit

Environment:
  COLLABHOST_VERSION    Same as -Version
'@
    return
}

# ---- Defaults ---------------------------------------------------------------

$Repo       = 'mrbildo/collabhost'
$Tag        = if ($Version) { $Version } elseif ($env:COLLABHOST_VERSION) { $env:COLLABHOST_VERSION } else { '' }

# ---- Platform detection -----------------------------------------------------

$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
switch ($arch)
{
    'X64'   { $Rid = 'win-x64';  $Ext = 'zip' }
    'Arm64' { throw 'win-arm64 is not supported in v1. See https://github.com/mrbildo/collabhost for status.' }
    default { throw "Unsupported architecture: $arch" }
}

# ---- Resolve tag / version --------------------------------------------------

if (-not $Tag)
{
    Write-Host 'Resolving latest release from GitHub...'
    $latest = Invoke-RestMethod -UseBasicParsing -Uri "https://api.github.com/repos/$Repo/releases/latest"
    $Tag = $latest.tag_name
    if (-not $Tag)
    {
        throw "Could not resolve latest tag from GitHub API."
    }
}

if ($Tag -notmatch '^v\d+\.\d+\.\d+$')
{
    throw "Invalid release tag '$Tag' -- expected vX.Y.Z."
}

$VersionNumber = $Tag.TrimStart('v')
$Archive       = "collabhost-$VersionNumber-$Rid.$Ext"
$BaseUrl       = "https://github.com/$Repo/releases/download/$Tag"
$ArchiveUrl    = "$BaseUrl/$Archive"
$ChecksumsUrl  = "$BaseUrl/checksums.txt"

# ---- Download + verify ------------------------------------------------------

$TmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ("collabhost-install-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $TmpDir -Force | Out-Null

try
{
    $ArchivePath   = Join-Path $TmpDir $Archive
    $ChecksumsPath = Join-Path $TmpDir 'checksums.txt'

    Write-Host "Downloading $Archive..."
    Invoke-WebRequest -UseBasicParsing -Uri $ArchiveUrl   -OutFile $ArchivePath

    Write-Host 'Downloading checksums.txt...'
    Invoke-WebRequest -UseBasicParsing -Uri $ChecksumsUrl -OutFile $ChecksumsPath

    Write-Host 'Verifying SHA-256...'
    # sha256sum output: "<hash>  <filename>" (two spaces). Match on filename in column 2.
    $expected = $null
    foreach ($line in Get-Content -LiteralPath $ChecksumsPath)
    {
        $parts = $line -split '\s+', 2
        if ($parts.Count -eq 2 -and $parts[1].Trim() -eq $Archive)
        {
            $expected = $parts[0].ToLowerInvariant()
            break
        }
    }

    if (-not $expected)
    {
        throw "Could not find checksum for $Archive in checksums.txt"
    }

    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $ArchivePath).Hash.ToLowerInvariant()
    if ($expected -ne $actual)
    {
        throw "Checksum mismatch for $Archive`n  Expected: $expected`n  Actual:   $actual"
    }

    # ---- Extract ------------------------------------------------------------

    $ExtractDir = Join-Path $TmpDir 'extract'
    New-Item -ItemType Directory -Path $ExtractDir -Force | Out-Null
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $ExtractDir -Force

    $ArchiveRoot = Join-Path $ExtractDir "collabhost-$VersionNumber-$Rid"
    if (-not (Test-Path -LiteralPath $ArchiveRoot -PathType Container))
    {
        throw "Archive layout unexpected: $ArchiveRoot not found after extract."
    }

    # ---- Install (reinstall-safe) -------------------------------------------

    if (-not (Test-Path -LiteralPath $InstallPath))
    {
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
    }

    # Overwrite files that are part of the bundle.
    Copy-Item -LiteralPath (Join-Path $ArchiveRoot 'collabhost.exe') -Destination $InstallPath -Force
    Copy-Item -LiteralPath (Join-Path $ArchiveRoot 'caddy.exe')      -Destination $InstallPath -Force
    Copy-Item -LiteralPath (Join-Path $ArchiveRoot 'INSTALL.md')     -Destination $InstallPath -Force

    $LicensesDst = Join-Path $InstallPath 'LICENSES'
    if (Test-Path -LiteralPath $LicensesDst)
    {
        Remove-Item -LiteralPath $LicensesDst -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $ArchiveRoot 'LICENSES') -Destination $InstallPath -Recurse -Force

    # Preserve appsettings.json if it already exists. Only seed from the archive
    # on first install. On upgrade the operator's edits survive (spec section 9.7, R2.1).
    $AppSettingsDst = Join-Path $InstallPath 'appsettings.json'
    if (-not (Test-Path -LiteralPath $AppSettingsDst))
    {
        Copy-Item -LiteralPath (Join-Path $ArchiveRoot 'appsettings.json') -Destination $InstallPath -Force
    }

    # data/ is never in the archive -- leave any existing directory untouched.
    # Merge-safe by construction.

    # ---- PATH integration ---------------------------------------------------

    if (-not $SkipPath)
    {
        $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
        $entries  = if ($userPath) { $userPath -split ';' } else { @() }
        if ($entries -notcontains $InstallPath)
        {
            $newPath = if ($userPath) { "$InstallPath;$userPath" } else { $InstallPath }
            [Environment]::SetEnvironmentVariable('PATH', $newPath, 'User')
            Write-Host "Added Collabhost to User PATH. Open a new terminal for it to take effect."
        }
    }

    # ---- Summary ------------------------------------------------------------

    Write-Host ''
    Write-Host "Collabhost $Tag installed to $InstallPath"
    Write-Host "Admin key: run 'collabhost' once to generate; first-run stdout captures it."
    Write-Host "See $InstallPath\INSTALL.md for configuration, env-var overrides, and upgrade notes."
}
finally
{
    if (Test-Path -LiteralPath $TmpDir)
    {
        Remove-Item -LiteralPath $TmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
