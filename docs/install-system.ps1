<#
.SYNOPSIS
    Collabhost install script (Windows) -- SYSTEM SCOPE.

.DESCRIPTION
    Lays the canonical Windows server layout under root-owned paths and
    registers Collabhost as a Windows Service running as LocalSystem.

      %ProgramFiles%\Collabhost\bin\collabhost.exe  -- binary
      %ProgramFiles%\Collabhost\bin\caddy.exe       -- bundled Caddy
      %ProgramFiles%\Collabhost\wwwroot\            -- frontend (Portal SPA)
      %ProgramFiles%\Collabhost\INSTALL.md          -- operator docs
      %ProgramFiles%\Collabhost\LICENSES\           -- bundled-binary license texts
      %ProgramData%\Collabhost\config\appsettings.json          -- operator config
      %ProgramData%\Collabhost\config\appsettings.shipped.json  -- smart-merge baseline
      %ProgramData%\Collabhost\data\                -- SQLite DB + backups
      %ProgramData%\Collabhost\user-types\          -- operator-authored AppType JSON
      %ProgramData%\Collabhost\caddy\               -- Caddy CA / cert storage
      %ProgramData%\Collabhost\logs\                -- crash logs

    Requires an administrator-elevated PowerShell. Re-runnable: on re-run,
    binaries / wwwroot / LICENSES are overwritten with the new release's
    contents; appsettings.json is smart-merged; data and Caddy storage are
    left untouched.

    Sister of install-system.sh (Linux system-scope). Self-contained by
    design -- the Windows shared-library extraction is deferred because the
    iwr-pipe-to-iex install path doesn't compose with a separate library file.

.EXAMPLE
    # Elevated PowerShell only:
    iwr -UseBasicParsing https://mrbildo.github.io/collabhost/install-system.ps1 | iex

.EXAMPLE
    .\install-system.ps1 -Version vX.Y.Z

.EXAMPLE
    # Pin via environment variable
    $env:COLLABHOST_VERSION = 'vX.Y.Z'; .\install-system.ps1

.EXAMPLE
    # Uninstall (preserve data)
    .\install-system.ps1 -Uninstall

.EXAMPLE
    # Uninstall and clear the operator database
    .\install-system.ps1 -Uninstall -PurgeData
#>

# Write-Host is intentional here -- this script is invoked via
# `iwr ... | iex` and must emit progress lines to the host console, not down
# the pipeline (which would be consumed by iex).
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '')]
[CmdletBinding()]
param
(
    [string]$Version,
    [switch]$Uninstall,
    [switch]$PurgeData,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'
# Speeds up Invoke-WebRequest by avoiding Internet Explorer engine probe.
$ProgressPreference = 'SilentlyContinue'

# ---- Canonical layout -------------------------------------------------------

# Hardcoded for v1 -- the whole point of this script is the canonical layout.
# If you need a custom layout, use install.ps1 + a hand-rolled service
# registration, or copy this script and edit the constants.
$ServiceName        = 'Collabhost'
$ServiceDisplayName = 'Collabhost'
$ServiceDescription = 'Collabhost self-hosted application platform.'
$InstallPrefix      = Join-Path $env:ProgramFiles  'Collabhost'
$BinDir             = Join-Path $InstallPrefix     'bin'
$DataRoot           = Join-Path $env:ProgramData   'Collabhost'
$ConfigDir          = Join-Path $DataRoot          'config'
$DataDir            = Join-Path $DataRoot          'data'
$UserTypesDir       = Join-Path $DataRoot          'user-types'
$CaddyStorageDir    = Join-Path $DataRoot          'caddy'
$LogDir             = Join-Path $DataRoot          'logs'
$AppSettingsPath    = Join-Path $ConfigDir         'appsettings.json'
$BaselinePath       = Join-Path $ConfigDir         'appsettings.shipped.json'

if ($Help)
{
    Write-Host @'
Collabhost installer (Windows) -- SYSTEM scope

Lays %ProgramFiles%\Collabhost + %ProgramData%\Collabhost and registers a
Windows Service running as LocalSystem. Requires an elevated PowerShell.

Usage: install-system.ps1 [options]

Options:
  -Version vX.Y.Z   Pin to a specific release tag (default: latest)
  -Uninstall        Stop + remove the service and clean up canonical paths.
  -PurgeData        With -Uninstall: also delete %ProgramData%\Collabhost\data\.
                    Default uninstall preserves data/ so a re-install picks up
                    the operator's existing database.
  -Help             Print this message and exit

Environment:
  COLLABHOST_VERSION           Same as -Version
  COLLABHOST_INSTALL_BASE_URL  Override archive download base URL (default: GitHub Releases)

For a per-user install in %USERPROFILE% (no admin required), see install.ps1.
'@
    return
}

# ---- Elevation guard --------------------------------------------------------

# Hard-fail if not elevated. Q8 ruling: no UAC re-launch dance -- the iwr-pipe-
# to-iex flow doesn't survive Start-Process -Verb RunAs cleanly (the elevated
# child spawns a new window; the original pipe-to-iex invocation is dead). The
# operator-honest shape is "this requires admin; run from an admin-elevated
# PowerShell."
function Test-IsElevated
{
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsElevated))
{
    [Console]::Error.WriteLine('install-system.ps1 requires an administrator-elevated PowerShell.')
    [Console]::Error.WriteLine('  Right-click PowerShell -> Run as administrator')
    [Console]::Error.WriteLine('  Or: Start-Process powershell -Verb RunAs')
    [Console]::Error.WriteLine('Then re-run the install command.')
    exit 1
}

# ---- Windows-only gate ------------------------------------------------------

if (-not $IsWindows -and $PSVersionTable.PSEdition -ne 'Desktop')
{
    [Console]::Error.WriteLine('install-system.ps1 is Windows-only.')
    [Console]::Error.WriteLine('  For Linux system-scope, use install-system.sh.')
    [Console]::Error.WriteLine('  For per-user installs on macOS, use install.sh.')
    exit 1
}

# ---- Service helpers --------------------------------------------------------

function Get-CollabhostService
{
    return Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
}

# Internal helper -- not a reusable cmdlet. The script as a whole is the
# state-changing surface; ShouldProcess plumbing here would just be ceremony.
function Stop-CollabhostServiceIfRunning
{
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '')]
    [CmdletBinding()]
    param
    (
        [int]$TimeoutSeconds = 30
    )

    $svc = Get-CollabhostService
    if (-not $svc)
    {
        return
    }

    if ($svc.Status -eq 'Stopped')
    {
        return
    }

    Write-Host "Stopping existing $ServiceName service..."
    try
    {
        Stop-Service -Name $ServiceName -Force -ErrorAction Stop
    }
    catch
    {
        Write-Host "  Stop-Service failed: $($_.Exception.Message). Falling through to wait + force-kill."
    }

    # Bounded wait. WaitForStatus throws on timeout -- catch and fall through to
    # the taskkill fallback for the "service is wedged" path (Anomaly F: Windows
    # file-locking requires stop-before-binary-replace; an upgrade cannot
    # proceed if the EXE is still mapped by the running process).
    try
    {
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds($TimeoutSeconds))
    }
    catch [System.ServiceProcess.TimeoutException]
    {
        Write-Host "  Service did not stop within ${TimeoutSeconds}s. Attempting taskkill /F fallback..."
        # Resolve the service PID via WMI (Get-CimInstance avoids the deprecated
        # Get-WmiObject cmdlet). A 0 ProcessId means "service stopped between
        # the wait timeout and this query" -- nothing to kill.
        $svcInfo = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
        if ($svcInfo -and $svcInfo.ProcessId -gt 0)
        {
            & taskkill.exe /F /PID $svcInfo.ProcessId 2>&1 | ForEach-Object { Write-Host "  $_" }
        }
        # Final wait -- if the kill stuck, this returns; if not, throw out.
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(10))
    }
}

# ---- Uninstall path ---------------------------------------------------------

if ($Uninstall)
{
    Write-Host "Uninstalling $ServiceName..."

    Stop-CollabhostServiceIfRunning

    if (Get-CollabhostService)
    {
        Write-Host "Removing $ServiceName service registration..."
        # Remove-Service exists on PS 6+; sc.exe delete is the PS 5.1 fallback.
        if (Get-Command Remove-Service -ErrorAction SilentlyContinue)
        {
            Remove-Service -Name $ServiceName -ErrorAction Stop
        }
        else
        {
            & sc.exe delete $ServiceName | Out-Null
            if ($LASTEXITCODE -ne 0)
            {
                throw "sc.exe delete $ServiceName failed with exit code $LASTEXITCODE."
            }
        }
    }

    # Always remove %ProgramFiles%\Collabhost (binaries + wwwroot + docs).
    if (Test-Path -LiteralPath $InstallPrefix)
    {
        Write-Host "Removing $InstallPrefix..."
        Remove-Item -LiteralPath $InstallPrefix -Recurse -Force
    }

    # Config + logs always go. Caddy storage always goes (re-issuable on next install).
    foreach ($removable in @($ConfigDir, $LogDir, $CaddyStorageDir, $UserTypesDir))
    {
        if (Test-Path -LiteralPath $removable)
        {
            Write-Host "Removing $removable..."
            Remove-Item -LiteralPath $removable -Recurse -Force
        }
    }

    if ($PurgeData)
    {
        if (Test-Path -LiteralPath $DataDir)
        {
            Write-Host "Removing $DataDir (-PurgeData)..."
            Remove-Item -LiteralPath $DataDir -Recurse -Force
        }
        # If %ProgramData%\Collabhost is now empty, remove it too.
        if ((Test-Path -LiteralPath $DataRoot) -and (-not (Get-ChildItem -LiteralPath $DataRoot -Force)))
        {
            Remove-Item -LiteralPath $DataRoot -Force
        }
    }
    else
    {
        if (Test-Path -LiteralPath $DataDir)
        {
            Write-Host "Preserved: $DataDir (use -PurgeData to clear)."
        }
    }

    Write-Host ''
    Write-Host "$ServiceName uninstalled."
    return
}

# ---- Install path -----------------------------------------------------------

# Retry wrapper for network calls. Mirrors `curl --retry 3 --retry-delay 2` in
# install.sh / parallel to install.ps1's helper. Only retries on transport /
# transient failures (WebException), NOT on HTTP 4xx (404s on a wrong tag
# should fail immediately).
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

$Repo = 'MrBildo/collabhost'
$Tag  = if ($Version) { $Version } elseif ($env:COLLABHOST_VERSION) { $env:COLLABHOST_VERSION } else { '' }

# ---- Platform detection -----------------------------------------------------

# install-system.ps1 is win-x64 only -- mirror install.ps1's rejection of
# win-arm64. The Linux system installer's parallel constraint is "Linux only,"
# enforced separately above.
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
        throw 'Could not resolve latest tag from GitHub API.'
    }
}

# Accepts vX.Y.Z and SemVer 2.0 §9 pre-release tags (e.g. v1.2.1-rc1, v2.0.0-beta.3).
# Build metadata (+...) is intentionally rejected -- archive filenames use the version
# as a path segment and '+' is friction across tools. Keep this pattern in sync with
# publish.yml, install-integration.yml, install-lib.sh, and install.ps1.
if ($Tag -notmatch '^v\d+\.\d+\.\d+(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$')
{
    throw "Invalid release tag '$Tag' -- expected vX.Y.Z or vX.Y.Z-<pre-release>."
}

$VersionNumber = $Tag.TrimStart('v')
$Archive       = "collabhost-$VersionNumber-$Rid.$Ext"
$BaseUrl       = if ($env:COLLABHOST_INSTALL_BASE_URL) { $env:COLLABHOST_INSTALL_BASE_URL } else { "https://github.com/$Repo/releases/download/$Tag" }
$ArchiveUrl    = "$BaseUrl/$Archive"
$ChecksumsUrl  = "$BaseUrl/checksums.txt"

# ---- Download + verify ------------------------------------------------------

$TmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ('collabhost-install-system-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $TmpDir -Force | Out-Null

try
{
    $ArchivePath   = Join-Path $TmpDir $Archive
    $ChecksumsPath = Join-Path $TmpDir 'checksums.txt'

    # Heartbeat: emit archive size from a HEAD request so the operator knows what
    # to expect during the silent download window. Same shape as install.ps1.
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
    # sha256sum output: "<hash>  <filename>" (two spaces). sha256sum -b emits a
    # leading '*' on the filename in column 2 -- strip it before comparison.
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

    $ExtractDir = Join-Path $TmpDir 'extract'
    New-Item -ItemType Directory -Path $ExtractDir -Force | Out-Null
    Write-Host 'Extracting archive...'
    Expand-Archive -LiteralPath $ArchivePath -DestinationPath $ExtractDir -Force

    $CollabhostSrc = Join-Path $ExtractDir 'collabhost.exe'
    if (-not (Test-Path -LiteralPath $CollabhostSrc -PathType Leaf))
    {
        throw 'Archive layout unexpected: collabhost.exe not found at archive root after extract.'
    }

    $PortalIndex = Join-Path $ExtractDir 'wwwroot\index.html'
    if (-not (Test-Path -LiteralPath $PortalIndex -PathType Leaf))
    {
        throw 'Archive layout unexpected: wwwroot\index.html not found after extract.'
    }

    # ---- Pre-existing install detection ------------------------------------

    # Detect a pre-existing install BEFORE touching anything. Used to emit the
    # "Preserved: ..." reassurance line on reinstalls.
    $IsReinstall = (Test-Path -LiteralPath $AppSettingsPath) -or (Test-Path -LiteralPath $DataDir)

    # Anomaly F: Windows file-locking requires stop-before-binary-replace. If
    # the service is registered AND running, we MUST stop it before overwriting
    # %ProgramFiles%\Collabhost\bin\collabhost.exe -- the OS holds an exclusive
    # lock on the EXE for the lifetime of the running process and Copy-Item
    # would fail with "The process cannot access the file because it is being
    # used by another process."
    Stop-CollabhostServiceIfRunning

    # ---- Layout ------------------------------------------------------------

    foreach ($dir in @($InstallPrefix, $BinDir, $DataRoot, $ConfigDir, $DataDir, $UserTypesDir, $CaddyStorageDir, $LogDir))
    {
        if (-not (Test-Path -LiteralPath $dir))
        {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
    }

    # ---- Install bundle artifacts ------------------------------------------

    Write-Host "Copying binaries to $BinDir..."
    Copy-Item -LiteralPath $CollabhostSrc                       -Destination (Join-Path $BinDir 'collabhost.exe') -Force
    Copy-Item -LiteralPath (Join-Path $ExtractDir 'caddy.exe')  -Destination (Join-Path $BinDir 'caddy.exe')      -Force
    Copy-Item -LiteralPath (Join-Path $ExtractDir 'INSTALL.md') -Destination (Join-Path $InstallPrefix 'INSTALL.md') -Force

    # LICENSES: clear + repopulate to stay in sync with archive.
    $LicensesDst = Join-Path $InstallPrefix 'LICENSES'
    if (-not (Test-Path -LiteralPath $LicensesDst))
    {
        New-Item -ItemType Directory -Path $LicensesDst -Force | Out-Null
    }
    Get-ChildItem -LiteralPath $LicensesDst -File -Force | Remove-Item -Force
    Copy-Item -Path (Join-Path $ExtractDir 'LICENSES\*') -Destination $LicensesDst -Force

    # wwwroot: always overwrite from the archive (Portal SPA, must track binary).
    $WwwrootDst = Join-Path $InstallPrefix 'wwwroot'
    if (Test-Path -LiteralPath $WwwrootDst)
    {
        Remove-Item -LiteralPath $WwwrootDst -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $ExtractDir 'wwwroot') -Destination $InstallPrefix -Recurse -Force

    # wwwroot.sha256 sidecar: build-time SHA-256 hash of the wwwroot/ tree,
    # written by the publish workflow. Lives at the install prefix root so the
    # UAT runbook can compare against /api/v1/version.wwwrootHash (#342).
    # Optional for archives predating #342 -- absence is silent.
    $WwwrootSidecarSrc = Join-Path $ExtractDir 'wwwroot.sha256'
    if (Test-Path -LiteralPath $WwwrootSidecarSrc)
    {
        Copy-Item -LiteralPath $WwwrootSidecarSrc -Destination $InstallPrefix -Force
    }

    # ---- Config (smart-merge) ----------------------------------------------

    # appsettings.json: smart-merge on upgrade, plain copy on first install.
    # Same shape as install.ps1 -- the binary owns the merge logic via
    # `collabhost --merge-appsettings`. Q6 ruling: copy the pattern; defer the
    # install-lib.ps1 extraction to v1.4.
    $ShippedSrc      = Join-Path $ExtractDir 'appsettings.json'
    $CollabhostExe   = Join-Path $BinDir 'collabhost.exe'

    if (-not (Test-Path -LiteralPath $AppSettingsPath))
    {
        # First install -- copy the shipped file and seed the baseline.
        Copy-Item -LiteralPath $ShippedSrc -Destination $AppSettingsPath -Force
        Copy-Item -LiteralPath $ShippedSrc -Destination $BaselinePath    -Force
    }
    else
    {
        # Upgrade -- run the smart-merge subcommand if the new binary supports it. The merge
        # subcommand shipped in v1.0.0; older binaries do not recognize the flag and would
        # fall through to starting the host. Feature-gate on --version output (stable since
        # pre-v0.1.0) and skip the merge for v0.x binaries -- in that scenario the current
        # behavior (preserve appsettings.json as bytes, no merge) is what the operator already
        # had. Match "Collabhost X.Y.Z" or "Collabhost vX.Y.Z" -- the optional 'v' guards
        # against any future change to VersionInfo.Current's prefix without re-breaking the
        # gate.
        $supportsMerge   = $false
        $versionLine     = $null
        $versionPattern  = '^Collabhost v?\d+\.\d+\.\d+'
        $probeFailed     = $false
        try
        {
            $versionLine = & $CollabhostExe --version 2>$null
            if ($LASTEXITCODE -eq 0 -and $versionLine -match $versionPattern)
            {
                $supportsMerge = $true
            }
        }
        catch
        {
            $probeFailed = $true
            Write-Verbose "Could not probe collabhost --version: $_"
        }

        if (-not $supportsMerge -and -not $probeFailed)
        {
            $observed = if ($versionLine) { $versionLine } else { '<empty>' }
            [Console]::Error.WriteLine('Warning: skipping appsettings.json smart-merge -- collabhost --version output did not match the expected pattern.')
            [Console]::Error.WriteLine("  Got:      $observed")
            [Console]::Error.WriteLine("  Expected: pattern '$versionPattern'")
            [Console]::Error.WriteLine('  Effect:   new shipped keys in appsettings.json may not be picked up automatically.')
            [Console]::Error.WriteLine("  See $AppSettingsPath and $ShippedSrc to reconcile by hand.")
        }

        if ($supportsMerge)
        {
            try
            {
                $mergeOutput = & $CollabhostExe --merge-appsettings $ShippedSrc $AppSettingsPath --baseline $BaselinePath 2>&1
                $exit = $LASTEXITCODE
                if ($mergeOutput) { $mergeOutput | ForEach-Object { Write-Host $_ } }
                if ($exit -ne 0)
                {
                    Write-Host "Warning: appsettings.json smart-merge exited with code $exit."
                    Write-Host "Your existing appsettings.json was left in place; new shipped defaults may not be picked up automatically."
                    Write-Host "See $AppSettingsPath and $ShippedSrc to reconcile by hand if needed."
                }
            }
            catch
            {
                Write-Host "Warning: appsettings.json smart-merge failed -- $($_.Exception.Message)"
                Write-Host "Your existing appsettings.json was left in place."
            }
        }
    }

    # Seed Proxy:BinaryPath in appsettings.json to the bundled caddy.exe path on
    # first install. On reinstall, leave the key alone -- if the operator pinned
    # an external Caddy, we respect that. (Q5 ruling: Caddy stays a Collabhost-
    # supervised child; Proxy:BinaryPath points at %ProgramFiles%\Collabhost\bin\caddy.exe.)
    $BundledCaddyPath = Join-Path $BinDir 'caddy.exe'
    try
    {
        $settings = Get-Content -LiteralPath $AppSettingsPath -Raw | ConvertFrom-Json
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
            Set-Content -LiteralPath $AppSettingsPath -Value $json -Encoding UTF8
        }
    }
    catch
    {
        Write-Host "Warning: could not seed Proxy:BinaryPath in appsettings.json -- $($_.Exception.Message)"
        Write-Host "Set COLLABHOST_PROXY_BINARY_PATH to '$BundledCaddyPath' or repair the file by hand."
    }

    if ($IsReinstall)
    {
        $DataHint = if (Test-Path -LiteralPath $DataDir) { ' and data/' } else { '' }
        Write-Host "Preserved your existing appsettings.json$DataHint."
    }

    # ---- SCM registration --------------------------------------------------

    # Q3 ruling: PowerShell New-Service for the registration call, sc.exe
    # follow-ups for crash-recovery + delayed-auto + network-wait. New-Service
    # exists on PS 5.1+ (Windows 10/11 stock) and is the cleaner shape inside
    # a PS script -- no trailing-space gotchas. Some advanced settings
    # (SERVICE_FAILURE_ACTIONS, delayed-auto, dependencies) are not exposed by
    # New-Service in PS 5.1, hence the sc.exe follow-up calls.
    #
    # The binPath is wrapped in double quotes inside the BinaryPathName so
    # paths containing spaces (the literal "%ProgramFiles%\Collabhost") are
    # parsed correctly by the SCM.
    $ServiceBinaryPath = '"' + (Join-Path $BinDir 'collabhost.exe') + '"'

    if (Get-CollabhostService)
    {
        Write-Host "$ServiceName service already registered -- updating binary path..."
        # New-Service has no Set-Service equivalent for BinaryPathName on PS 5.1
        # (Set-Service -BinaryPathName landed in PS 6.0). sc.exe config covers
        # both PS versions cleanly. Note: sc.exe's "key= value" syntax requires
        # a space AFTER the '=' (a leading space in PS argument parsing strips
        # the second token; `binPath= "..."` is two tokens, NOT one).
        & sc.exe config $ServiceName binPath= $ServiceBinaryPath start= delayed-auto obj= LocalSystem | Out-Null
        if ($LASTEXITCODE -ne 0)
        {
            throw "sc.exe config $ServiceName failed with exit code $LASTEXITCODE."
        }
    }
    else
    {
        Write-Host "Registering $ServiceName service..."
        New-Service `
            -Name           $ServiceName `
            -DisplayName    $ServiceDisplayName `
            -Description    $ServiceDescription `
            -BinaryPathName $ServiceBinaryPath `
            -StartupType    Automatic | Out-Null

        # Network-wait posture: depend on Tcpip + delayed-auto so the service
        # starts after the network stack is up. Mirrors the Linux unit's
        # After=network-online.target / Wants=network-online.target. sc.exe
        # config writes are idempotent; running on every install is harmless.
        & sc.exe config $ServiceName start= delayed-auto depend= Tcpip | Out-Null
        if ($LASTEXITCODE -ne 0)
        {
            throw "sc.exe config $ServiceName start/depend failed with exit code $LASTEXITCODE."
        }
    }

    # Crash-recovery: restart on first + second failure, take no action on the
    # third (operator inspects logs). Reset the failure count after 1 day so a
    # genuine crash-loop trips after three failures, not three lifetime ones.
    # Mirrors systemd's Restart=on-failure + StartLimitBurst=5 / StartLimitIntervalSec=60
    # (delays here are in milliseconds -- 5000ms == 5s, parallel to RestartSec=5).
    Write-Host 'Configuring crash-recovery actions...'
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000// | Out-Null
    if ($LASTEXITCODE -ne 0)
    {
        throw "sc.exe failure $ServiceName failed with exit code $LASTEXITCODE."
    }

    # ---- Service-scoped environment variables ------------------------------

    # Point the binary at the canonical Windows system layout. Each path mirrors
    # what install-system.sh sets in the systemd unit's [Service] block (lines
    # 363-385 of that script) -- this is the Windows analog of the Linux
    # Environment="..." directives. The SCM reads these at service start and
    # injects them into the binary's process environment before ExecStart.
    #
    # Card #246 (c2-A) baked the system-scope contract: when ASPNETCORE_CONTENTROOT
    # is set, the binary resolves ContentRoot to %ProgramFiles%\Collabhost so
    # wwwroot/ resolves correctly; COLLABHOST_CONFIG_PATH points the explicit
    # AddJsonFile call at %ProgramData%\Collabhost\config\appsettings.json. The
    # operator-facing path env vars (DATA, USER_TYPES, LOGS, PROXY_STORAGE)
    # override the shipped appsettings.json defaults so SQLite, user-types,
    # crash logs, and Caddy storage land under %ProgramData% (where uninstall
    # preserves them) rather than ContentRoot-relative under %ProgramFiles%.
    #
    # ASPNETCORE_ENVIRONMENT / DOTNET_ENVIRONMENT=Production guard against
    # machine-scoped Development overrides leaking into the service.
    #
    # Shape: REG_MULTI_SZ value named "Environment" under the service key. The
    # SCM honors this convention on Win10+/Server 2016+. Card #277.
    $ServiceRegistryKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    $ServiceEnvVars = @(
        "ASPNETCORE_CONTENTROOT=$InstallPrefix",
        "COLLABHOST_CONFIG_PATH=$AppSettingsPath",
        "COLLABHOST_DATA_PATH=$DataDir",
        "COLLABHOST_USER_TYPES_PATH=$UserTypesDir",
        "COLLABHOST_LOGS_PATH=$LogDir",
        "COLLABHOST_PROXY_STORAGE_PATH=$CaddyStorageDir",
        'ASPNETCORE_ENVIRONMENT=Production',
        'DOTNET_ENVIRONMENT=Production'
    )

    Write-Host 'Setting service environment variables...'
    # Set-ItemProperty on a missing value creates it; on an existing value
    # replaces it. PropertyType MultiString = REG_MULTI_SZ. Idempotent across
    # reinstalls -- if the v1.3.0 set ever grows (e.g. a new path var lands in
    # v1.4), the next install replaces the value with the new set rather than
    # appending.
    Set-ItemProperty `
        -LiteralPath $ServiceRegistryKey `
        -Name        'Environment' `
        -Value       $ServiceEnvVars `
        -Type        MultiString `
        -ErrorAction Stop

    # ---- Start service -----------------------------------------------------

    Write-Host "Starting $ServiceName..."
    Start-Service -Name $ServiceName -ErrorAction Stop

    # ---- Summary -----------------------------------------------------------

    # Resolve bundled Caddy version for the Bundled: disclosure line. Non-fatal --
    # if the binary won't execute, fall back to omitting the version number.
    $CaddyVersion = ''
    try
    {
        $CaddyVersionRaw = & (Join-Path $BinDir 'caddy.exe') version 2>$null
        if ($CaddyVersionRaw)
        {
            $CaddyVersion = ($CaddyVersionRaw -split '\s+')[0]
        }
    }
    catch
    {
        Write-Verbose "caddy version failed: $_"
    }

    $BundledLine = if ($CaddyVersion) {
        "Bundled: collabhost $Tag + Caddy $CaddyVersion"
    } else {
        "Bundled: collabhost $Tag + Caddy (bundled)"
    }

    Write-Host ''
    Write-Host "Collabhost $Tag installed system-wide."
    Write-Host $BundledLine
    Write-Host "  Binaries:    $BinDir\"
    Write-Host "  Config:      $AppSettingsPath"
    Write-Host "  Data:        $DataDir\"
    Write-Host "  Logs:        $LogDir\"
    Write-Host "  Service:     $ServiceName  (sc.exe query $ServiceName)"
    Write-Host ''
    if ($IsReinstall)
    {
        Write-Host 'Reinstall: data and Caddy storage preserved; binaries + wwwroot refreshed.'
    }
    else
    {
        Write-Host 'First boot: collabhost prints its admin key once at startup.'
        Write-Host '  Under the Windows Service, the key surfaces in the Application event log:'
        Write-Host '    Get-WinEvent -LogName Application -MaxEvents 200 |'
        Write-Host "      Where-Object { `$_.ProviderName -like 'collabhost*' -and `$_.Message -match 'Collabhost admin key:' } |"
        Write-Host '      Select-Object -First 1 -ExpandProperty Message'
        Write-Host "  See $InstallPrefix\INSTALL.md section 2 for details."
    }
    Write-Host ''
    Write-Host 'Verify with:'
    Write-Host "  sc.exe query $ServiceName"
    Write-Host '  curl http://localhost:58400/api/v1/status'
    Write-Host ''
    Write-Host "After registering apps, run 'Start-Process collabhost -ArgumentList ''--update-hosts'' -Verb RunAs' so <slug>.collab.internal resolves from this host. See $InstallPrefix\INSTALL.md section 9.10.2."
    Write-Host ''
    Write-Host 'Uninstall with:'
    Write-Host '  .\install-system.ps1 -Uninstall              # preserves %ProgramData%\Collabhost\data\'
    Write-Host '  .\install-system.ps1 -Uninstall -PurgeData   # also clears the operator database'
}
finally
{
    if (Test-Path -LiteralPath $TmpDir)
    {
        Remove-Item -LiteralPath $TmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
