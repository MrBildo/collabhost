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

# Retry wrapper for network calls. Mirrors `curl --retry 3 --retry-delay 2` in
# install.sh. Only retries on transport/transient failures (WebException),
# NOT on HTTP 4xx (404s on a wrong tag should fail immediately).
function Invoke-WithRetry
{
    param
    (
        [Parameter(Mandatory = $true)]
        [scriptblock]$Script,
        [int]$Retries = 3,
        [int]$DelaySeconds = 2
    )

    $attempt = 0
    while ($true)
    {
        $attempt++
        try
        {
            return & $Script
        }
        catch [System.Net.WebException]
        {
            $statusCode = $null
            if ($_.Exception.Response)
            {
                $statusCode = [int]$_.Exception.Response.StatusCode
            }
            # 4xx errors are the caller's problem; don't retry.
            if ($statusCode -and $statusCode -ge 400 -and $statusCode -lt 500)
            {
                throw
            }
            if ($attempt -ge $Retries)
            {
                throw
            }
            Start-Sleep -Seconds $DelaySeconds
        }
    }
}

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
  COLLABHOST_VERSION           Same as -Version
  COLLABHOST_INSTALL_BASE_URL  Override archive download base URL (default: GitHub Releases)
'@
    return
}

# ---- Defaults ---------------------------------------------------------------

$Repo       = 'MrBildo/collabhost'
$Tag        = if ($Version) { $Version } elseif ($env:COLLABHOST_VERSION) { $env:COLLABHOST_VERSION } else { '' }

# ---- Platform detection -----------------------------------------------------

$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
switch ($arch)
{
    'X64'   { $Rid = 'win-x64';  $Ext = 'zip' }
    'Arm64' { throw 'win-arm64 is not supported in v1. See https://github.com/MrBildo/collabhost for status.' }
    default { throw "Unsupported architecture: $arch" }
}

# ---- Resolve tag / version --------------------------------------------------

if (-not $Tag)
{
    Write-Host 'Resolving latest release from GitHub...'
    $latest = Invoke-WithRetry -Script {
        Invoke-RestMethod -UseBasicParsing -Uri "https://api.github.com/repos/$Repo/releases/latest"
    }
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
$BaseUrl       = if ($env:COLLABHOST_INSTALL_BASE_URL) { $env:COLLABHOST_INSTALL_BASE_URL } else { "https://github.com/$Repo/releases/download/$Tag" }
$ArchiveUrl    = "$BaseUrl/$Archive"
$ChecksumsUrl  = "$BaseUrl/checksums.txt"

# ---- Download + verify ------------------------------------------------------

$TmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ("collabhost-install-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $TmpDir -Force | Out-Null

try
{
    $ArchivePath   = Join-Path $TmpDir $Archive
    $ChecksumsPath = Join-Path $TmpDir 'checksums.txt'

    # Heartbeat: emit archive size from a HEAD request so the operator knows what
    # to expect during the silent download window. The same HEAD response also
    # serves as a pre-flight existence check -- a 404 here means the version tag
    # does not exist on the release server (typo, deleted release, pre-release tag
    # that passed the regex). Fatal on 404; non-fatal on all other failures so that
    # a transient network error does not block the install.
    $SizeHint = ''
    try
    {
        $headResponse = Invoke-WithRetry -Script {
            Invoke-WebRequest -UseBasicParsing -Method Head -Uri $ArchiveUrl
        }
        # PS7 returns Headers['Content-Length'] as String[], PS5.1 as a scalar.
        # @(...)[0] coerces either shape to a scalar string safely.
        $contentLength = @($headResponse.Headers['Content-Length'])[0]
        if ($contentLength -and [long]$contentLength -gt 0)
        {
            $sizeMb = [Math]::Round([long]$contentLength / 1MB)
            $SizeHint = " (~$sizeMb MB)"
        }
    }
    catch
    {
        # Check for a 404 before treating this as a non-fatal size-hint failure.
        # A 404 means the tag does not exist -- tell the operator clearly so they
        # can correct the version string. Any other failure leaves SizeHint empty
        # and proceeds without the parenthetical.
        $statusCode = $null
        if ($_.Exception.Response)
        {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        if ($statusCode -eq 404)
        {
            throw "Release tag '$Tag' not found. See https://github.com/$Repo/releases for available versions."
        }
        Write-Verbose "Content-Length HEAD failed: $_"
    }

    Write-Host "Downloading $Archive$SizeHint..."
    Invoke-WithRetry -Script {
        Invoke-WebRequest -UseBasicParsing -Uri $ArchiveUrl -OutFile $ArchivePath
    } | Out-Null

    Write-Host 'Downloading checksums.txt...'
    Invoke-WithRetry -Script {
        Invoke-WebRequest -UseBasicParsing -Uri $ChecksumsUrl -OutFile $ChecksumsPath
    } | Out-Null

    Write-Host 'Verifying SHA-256...'
    # sha256sum output: "<hash>  <filename>" (two spaces). Match on filename
    # in column 2. sha256sum -b (binary mode, used by Git-Bash on Windows)
    # emits "<hash>  *<filename>" -- strip the leading '*' before comparison
    # so the installer tolerates either mode regardless of what published
    # the checksum.
    $expected = $null
    foreach ($line in Get-Content -LiteralPath $ChecksumsPath)
    {
        $parts = $line -split '\s+', 2
        if ($parts.Count -eq 2)
        {
            $name = $parts[1].Trim().TrimStart('*')
            if ($name -eq $Archive)
            {
                $expected = $parts[0].ToLowerInvariant()
                break
            }
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

    # ---- Archive content pre-check -----------------------------------------

    # Guard against zero-byte or HTML-error downloads. zip magic is 'PK' (0x50 0x4b).
    $archiveSize = (Get-Item -LiteralPath $ArchivePath).Length
    if ($archiveSize -lt 1024)
    {
        throw "Archive $Archive looks truncated ($archiveSize bytes)."
    }

    $magic = New-Object byte[] 2
    $fs = [System.IO.File]::OpenRead($ArchivePath)
    try
    {
        $read = $fs.Read($magic, 0, 2)
    }
    finally
    {
        $fs.Dispose()
    }
    if ($read -lt 2 -or $magic[0] -ne 0x50 -or $magic[1] -ne 0x4B)
    {
        $hex = ($magic | ForEach-Object { '{0:x2}' -f $_ }) -join ''
        throw "Archive $Archive is not a valid zip file (magic=$hex)."
    }

    # ---- Extract ------------------------------------------------------------

    # The archive is flat -- seven items sit at the archive root (six files/dirs
    # plus wwwroot/), no wrapping directory. Extract straight into ExtractDir
    # and copy from there.
    $ExtractDir = Join-Path $TmpDir 'extract'
    New-Item -ItemType Directory -Path $ExtractDir -Force | Out-Null
    Write-Host 'Extracting archive...'
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $ExtractDir -Force

    $CollabhostSrc = Join-Path $ExtractDir 'collabhost.exe'
    if (-not (Test-Path -LiteralPath $CollabhostSrc -PathType Leaf))
    {
        throw "Archive layout unexpected: collabhost.exe not found at archive root after extract."
    }

    $PortalIndex = Join-Path $ExtractDir 'wwwroot\index.html'
    if (-not (Test-Path -LiteralPath $PortalIndex -PathType Leaf))
    {
        throw "Archive layout unexpected: wwwroot\index.html not found after extract."
    }

    # ---- Install (reinstall-safe) -------------------------------------------

    # Detect a pre-existing install BEFORE touching anything. Used to emit the
    # "Preserved: ..." reassurance line on reinstalls.
    $AppSettingsDst = Join-Path $InstallPath 'appsettings.json'
    $DataDirDst     = Join-Path $InstallPath 'data'
    $IsReinstall    = (Test-Path -LiteralPath $AppSettingsDst) -or (Test-Path -LiteralPath $DataDirDst)

    if (-not (Test-Path -LiteralPath $InstallPath))
    {
        New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
    }

    # Overwrite files that are part of the bundle.
    Copy-Item -LiteralPath $CollabhostSrc                         -Destination $InstallPath -Force
    Copy-Item -LiteralPath (Join-Path $ExtractDir 'caddy.exe')    -Destination $InstallPath -Force
    Copy-Item -LiteralPath (Join-Path $ExtractDir 'INSTALL.md')   -Destination $InstallPath -Force

    # LICENSES: mirror bash -- keep the directory, clear its contents, copy new
    # files into it. Avoids the "Copy-Item -Recurse into an existing dir nests
    # LICENSES/LICENSES/" pitfall.
    $LicensesDst = Join-Path $InstallPath 'LICENSES'
    if (-not (Test-Path -LiteralPath $LicensesDst))
    {
        New-Item -ItemType Directory -Path $LicensesDst -Force | Out-Null
    }
    Get-ChildItem -LiteralPath $LicensesDst -File -Force | Remove-Item -Force
    Copy-Item -Path (Join-Path $ExtractDir 'LICENSES\*') -Destination $LicensesDst -Force

    # appsettings.json: smart-merge on upgrade, plain copy on first install.
    #
    # First install: copy the archive's shipped appsettings.json into place AND seed the sidecar
    # baseline (appsettings.shipped.json) so the next upgrade has a reference for distinguishing
    # operator-edited keys from untouched defaults (card #161).
    #
    # Upgrade: invoke `collabhost --merge-appsettings <shipped> <ondisk> --baseline <baseline>`
    # to perform the three-way merge. The new binary owns the merge logic so the same shape runs
    # on every platform without duplicating JSON-handling code in PS + bash.
    $ShippedSrc      = Join-Path $ExtractDir 'appsettings.json'
    $BaselineDst     = Join-Path $InstallPath 'appsettings.shipped.json'
    $CollabhostExe   = Join-Path $InstallPath 'collabhost.exe'

    if (-not (Test-Path -LiteralPath $AppSettingsDst))
    {
        # First install -- copy the shipped file and seed the baseline.
        Copy-Item -LiteralPath $ShippedSrc -Destination $AppSettingsDst -Force
        Copy-Item -LiteralPath $ShippedSrc -Destination $BaselineDst    -Force
    }
    else
    {
        # Upgrade -- run the smart-merge subcommand if the new binary supports it. The merge
        # subcommand shipped in v1.0.0; older binaries do not recognize the flag and would fall
        # through to starting the host. Feature-gate on --version output (stable since pre-v0.1.0)
        # and skip the merge for v0.x binaries -- in that scenario the current behavior (preserve
        # appsettings.json as bytes, no merge) is what the operator already had.
        $supportsMerge = $false
        try
        {
            $versionLine = & $CollabhostExe --version 2>$null
            if ($LASTEXITCODE -eq 0 -and $versionLine -match '^Collabhost v[1-9][0-9]*\.')
            {
                $supportsMerge = $true
            }
        }
        catch
        {
            # Non-fatal -- treat as unsupported and skip the merge.
            Write-Verbose "Could not probe collabhost --version: $_"
        }

        if ($supportsMerge)
        {
            try
            {
                $mergeOutput = & $CollabhostExe --merge-appsettings $ShippedSrc $AppSettingsDst --baseline $BaselineDst 2>&1
                $exit = $LASTEXITCODE
                if ($mergeOutput) { $mergeOutput | ForEach-Object { Write-Host $_ } }
                if ($exit -ne 0)
                {
                    Write-Host "Warning: appsettings.json smart-merge exited with code $exit."
                    Write-Host "Your existing appsettings.json was left in place; new shipped defaults may not be picked up automatically."
                    Write-Host "See $InstallPath\appsettings.json and $ShippedSrc to reconcile by hand if needed."
                }
            }
            catch
            {
                # Non-fatal: a failed merge leaves appsettings.json untouched (the merger writes
                # atomically), and the operator's existing config remains valid.
                Write-Host "Warning: appsettings.json smart-merge failed -- $($_.Exception.Message)"
                Write-Host "Your existing appsettings.json was left in place."
            }
        }
    }

    if ($IsReinstall)
    {
        $DataHint = if (Test-Path -LiteralPath $DataDirDst) { ' and data/' } else { '' }
        Write-Host "Preserved your existing appsettings.json$DataHint."
    }

    # Seed Proxy:BinaryPath in appsettings.json to the bundled caddy.exe path on
    # first install. On reinstall, leave the key alone -- if the operator pinned
    # an external Caddy, we respect that. Smart-merge is intentionally minimal:
    # absent -> seed; present (any value) -> leave alone.
    $BundledCaddyPath = Join-Path $InstallPath 'caddy.exe'
    try
    {
        $settings = Get-Content -LiteralPath $AppSettingsDst -Raw | ConvertFrom-Json
        if (-not $settings.PSObject.Properties.Match('Proxy').Count)
        {
            $settings | Add-Member -MemberType NoteProperty -Name 'Proxy' -Value ([pscustomobject]@{})
        }
        $proxyHasBinaryPath = [bool]$settings.Proxy.PSObject.Properties.Match('BinaryPath').Count
        $existingBinaryPath = if ($proxyHasBinaryPath) { $settings.Proxy.BinaryPath } else { $null }
        if (-not $proxyHasBinaryPath -or [string]::IsNullOrWhiteSpace($existingBinaryPath))
        {
            if ($proxyHasBinaryPath)
            {
                $settings.Proxy.BinaryPath = $BundledCaddyPath
            }
            else
            {
                $settings.Proxy | Add-Member -MemberType NoteProperty -Name 'BinaryPath' -Value $BundledCaddyPath
            }
            $json = $settings | ConvertTo-Json -Depth 32
            Set-Content -LiteralPath $AppSettingsDst -Value $json -Encoding UTF8
        }
    }
    catch
    {
        # Non-fatal -- if we can't parse appsettings.json (operator has hand-edited it
        # into invalid JSON, for instance), fall through. The operator can set
        # COLLABHOST_CADDY_PATH or fix the file by hand. Surface the failure so they
        # know to investigate.
        Write-Host "Warning: could not seed Proxy:BinaryPath in appsettings.json -- $($_.Exception.Message)"
        Write-Host "Set COLLABHOST_CADDY_PATH to '$BundledCaddyPath' or repair the file by hand."
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

    # Resolve bundled Caddy version for the Bundled: disclosure line. Non-fatal --
    # if the binary won't execute (exec-bit, antivirus, exotic platform), fall back
    # to omitting the version number.
    $CaddyExe     = Join-Path $InstallPath 'caddy.exe'
    $CaddyVersion = ''
    try
    {
        $CaddyVersionRaw = & $CaddyExe version 2>$null
        if ($CaddyVersionRaw)
        {
            $CaddyVersion = ($CaddyVersionRaw -split '\s+')[0]
        }
    }
    catch
    {
        # Non-fatal -- Caddy version is best-effort. Proceed without it.
        Write-Verbose "caddy version failed: $_"
    }

    $BundledLine = if ($CaddyVersion) {
        "Bundled: collabhost $Tag + Caddy $CaddyVersion"
    } else {
        "Bundled: collabhost $Tag + Caddy (bundled)"
    }

    Write-Host ''
    Write-Host "Collabhost $Tag installed to $InstallPath"
    Write-Host $BundledLine
    if ($IsReinstall)
    {
        Write-Host 'Restart Collabhost to pick up the new binary. Your admin key and configuration are preserved.'
    }
    else
    {
        Write-Host "Next: open a new terminal and run 'collabhost'. On first boot it prints your admin key -- copy it immediately."
    }
    Write-Host "See $InstallPath\INSTALL.md for configuration, env-var overrides, and upgrade notes."
}
finally
{
    if (Test-Path -LiteralPath $TmpDir)
    {
        Remove-Item -LiteralPath $TmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
