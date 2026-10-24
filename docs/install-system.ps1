<#
.SYNOPSIS
    Collabhost install script (Windows) -- SYSTEM SCOPE.

.DESCRIPTION
    Lays the canonical Windows server layout and registers Collabhost as a
    Windows Service running under the dedicated virtual account
    'NT SERVICE\Collabhost'. The %ProgramFiles% side is read-only to that
    account. %ProgramData%\Collabhost does not inherit %ProgramData%'s ACL:
    only SYSTEM, Administrators and the service account can reach it, and the
    account is granted Modify per writable directory and Read on config\.
    This is the least-privilege Windows analog of the Linux system install's
    dedicated 'collabhost' service user and 0750 data directories.

      %ProgramFiles%\Collabhost\bin\collabhost.exe  -- binary (read-only to service)
      %ProgramFiles%\Collabhost\bin\caddy.exe       -- bundled Caddy (read-only to service)
      %ProgramFiles%\Collabhost\wwwroot\            -- frontend (Portal SPA, read-only to service)
      %ProgramFiles%\Collabhost\INSTALL.md          -- operator docs
      %ProgramFiles%\Collabhost\LICENSES\           -- bundled-binary license texts
      %ProgramData%\Collabhost\config\appsettings.json          -- operator config (service R)
      %ProgramData%\Collabhost\config\appsettings.shipped.json  -- smart-merge baseline
      %ProgramData%\Collabhost\data\                -- SQLite DB + backups (service R/W)
      %ProgramData%\Collabhost\user-types\          -- operator-authored AppType JSON (service R/W)
      %ProgramData%\Collabhost\caddy\               -- Caddy CA / cert storage (service R/W)
      %ProgramData%\Collabhost\logs\                -- crash logs (service R/W)
      %ProgramData%\Collabhost\dotnet-bundle\       -- single-file native-lib extract dir (service R/W)

    Requires an administrator-elevated PowerShell. Re-runnable: on re-run,
    binaries / wwwroot / LICENSES are overwritten with the new release's
    contents; appsettings.json is smart-merged; data and Caddy storage are
    left in place, and the %ProgramData%\Collabhost permissions are re-applied
    (so re-running over an install made by an older script corrects them).

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

# Windows PowerShell 5.1 runs on .NET Framework. When the framework is set to an
# explicit protocol list (older machines, or a policy) that list can leave
# TLS 1.2 out, and every GitHub download then fails. In that case use TLS 1.2.
# The OS default (SystemDefault, value 0) is left alone: it already negotiates
# TLS 1.2 or newer on supported Windows versions. PowerShell 7 always uses the
# OS default.
if ($PSVersionTable.PSVersion.Major -lt 6)
{
    $currentProtocols = [Net.ServicePointManager]::SecurityProtocol
    if ([int]$currentProtocols -ne 0 -and -not ($currentProtocols -band [Net.SecurityProtocolType]::Tls12))
    {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    }
}

# ---- Canonical layout -------------------------------------------------------

# Hardcoded for v1 -- the whole point of this script is the canonical layout.
# If you need a custom layout, use install.ps1 + a hand-rolled service
# registration, or copy this script and edit the constants.
$ServiceName        = 'Collabhost'
$ServiceDisplayName = 'Collabhost'
$ServiceDescription = 'Collabhost self-hosted application platform.'

# The service runs under its own auto-managed virtual account rather than
# LocalSystem. A virtual account ('NT SERVICE\<service-name>') needs no
# password and no separate creation step -- the SCM provisions the SID and
# grants the logon right when the service's logon identity is set to it. It is
# a limited account: %ProgramFiles% stays un-writable by default, and the
# writable %ProgramData% paths are granted to it explicitly below. This mirrors
# the Linux system install's dedicated 'collabhost' user.
$ServiceAccount     = "NT SERVICE\$ServiceName"

$InstallPrefix      = Join-Path $env:ProgramFiles  'Collabhost'
$BinDir             = Join-Path $InstallPrefix     'bin'
$DataRoot           = Join-Path $env:ProgramData   'Collabhost'
$ConfigDir          = Join-Path $DataRoot          'config'
$DataDir            = Join-Path $DataRoot          'data'
$UserTypesDir       = Join-Path $DataRoot          'user-types'
$CaddyStorageDir    = Join-Path $DataRoot          'caddy'
$LogDir             = Join-Path $DataRoot          'logs'

# Single-file native-library extraction target. The shipped binary is a
# PublishSingleFile self-contained build with native-lib self-extract; on first
# start it unpacks native libraries to a writable directory. LocalSystem could
# fall back to a writable default temp; a limited virtual account cannot rely on
# that, so we give it a granted directory and point DOTNET_BUNDLE_EXTRACT_BASE_DIR
# at it (the Windows analog of the Linux unit's dotnet-bundle dir + env var).
$DotnetBundleDir    = Join-Path $DataRoot          'dotnet-bundle'

$AppSettingsPath    = Join-Path $ConfigDir         'appsettings.json'
$BaselinePath       = Join-Path $ConfigDir         'appsettings.shipped.json'

# %ProgramData%\Collabhost is not left with the ACL it would inherit from
# %ProgramData%: that ACL lets every local user read files (the admin key in
# config, the database, the Caddy CA private key) and create files and folders
# (including in dotnet-bundle\, where the runtime loads native libraries from).
# The installer breaks inheritance at $DataRoot so only SYSTEM, Administrators
# and the service account have access, and re-applies that on every run so an
# upgrade fixes an install laid down by an older script (see
# Protect-CollabhostDataRoot and Grant-CollabhostServiceAccess below).
#
# The %ProgramData% subdirectories the service account is granted Modify on.
# %ProgramFiles% is deliberately absent: it stays read-only to the account.
# config/ is also absent: the service never writes config at runtime, so it is
# granted Read below rather than Modify (see $ReadOnlyServiceDirs).
$WritableServiceDirs = @($DataDir, $UserTypesDir, $CaddyStorageDir, $LogDir, $DotnetBundleDir)

# The %ProgramData% subdirectories the service account is granted read-only.
# config/ holds appsettings.json, which the service only ever reads (it reads
# Auth:AdminKey and the rest of the config at startup). It never writes config
# at runtime: the --merge-appsettings write runs install-time under the elevated
# installer before the host builds, and settings persistence is DB-backed. This
# matches the Linux install's read-only appsettings.json posture (0640
# root:collabhost -- read-only to the service user).
$ReadOnlyServiceDirs = @($ConfigDir)

if ($Help)
{
    Write-Host @'
Collabhost installer (Windows) -- SYSTEM scope

Lays %ProgramFiles%\Collabhost + %ProgramData%\Collabhost and registers a
Windows Service running under the virtual account 'NT SERVICE\Collabhost'.
Requires an elevated PowerShell.

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
  COLLABHOST_INSTALL_BASE_URL  Override archive download base URL (default: GitHub Releases).
                               Must be https:// (http://localhost and http://127.0.0.1 are accepted).
  COLLABHOST_INSTALL_ALLOW_HTTP=1  Accept a plain http:// base URL pointing at another host.

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

# ---- Least-privilege ACL helpers --------------------------------------------

# Run icacls and fail loud on any error. None of these calls pass /C: with /C,
# icacls reports a per-item failure only in its text output and can still exit
# 0, so a partly applied ACL would look like success. Without /C it stops at the
# first failing item and exits non-zero, which this turns into a terminating
# error (the service is stopped while this runs, so nothing should hold a lock).
function Invoke-IcaclsCommand
{
    param
    (
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $output = & icacls.exe @Arguments 2>&1
    if ($LASTEXITCODE -ne 0)
    {
        throw "icacls $($Arguments -join ' ') failed with exit code $LASTEXITCODE.`n$($output -join [Environment]::NewLine)"
    }
}

# Junctions and symbolic links under a tree, the root included. Collabhost never
# creates one in its data or install tree, and icacls /T walks through a directory
# junction into its target and changes the ACLs there, with or without /L
# (inheritance set on a parent directory does not cross one). Whoever could write
# into the tree before this installer restricted it (any local user on an older
# system install, the service account on an older user-scope install) could create
# a junction, and an elevated ACL change would then land on whatever it points at.
# The installer refuses to go on instead (see Get-LinkRefusalMessage).
# -ErrorAction Stop: a directory that cannot be listed could hide a link, so it is
# an error here, not something to skip.
function Get-LinkInTree
{
    param
    (
        [Parameter(Mandatory = $true)][string]$Path
    )

    $items = @(Get-Item -LiteralPath $Path -Force) + @(Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction Stop)
    return @($items | Where-Object { $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint } | ForEach-Object { $_.FullName })
}

function Get-LinkRefusalMessage
{
    param
    (
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string[]]$Links,
        [Parameter(Mandatory = $true)][string]$State
    )

    $list = ($Links | ForEach-Object { "  $_" }) -join [Environment]::NewLine
    return @"
Found a junction or symbolic link inside ${Path}:
$list
Collabhost never creates links there, and this installer will not change permissions
through one: the change would land on whatever the link points at. $State
If you did not create the link, delete the link itself and re-run the installer
('rmdir <path>' for a directory link, 'del <path>' for a file link; neither touches
the target). If you created it to keep data elsewhere, move the data back into
$Path and set the location in appsettings.json instead.
"@
}

# Take back, one item at a time, anything under %ProgramData%\Collabhost that this
# installer cannot simply reset: items Administrators cannot list or read, items
# with inheritance switched off or a Deny entry, and items owned by anyone other
# than SYSTEM, Administrators, TrustedInstaller, the account running this
# installer or the service account. While older installs left the tree writable
# by every local user, any of them could create such an item, and one that
# excludes Administrators would otherwise stop the recursive lockdown halfway.
# Each item gets 'takeown /A' (Administrators become the owner, using the
# take-ownership privilege, so it works whatever the item's ACL says) and then a
# reset to inherit from its parent. Neither call recurses or prompts. A directory
# that could not be listed is listable after that, so the scan repeats until it
# finds nothing. Runs BEFORE the service is stopped and refuses links (see
# Get-LinkInTree), so if it throws, the running install is left as it was.
function Repair-CollabhostDataRoot
{
    param
    (
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$ServiceAccount
    )

    if (-not (Test-Path -LiteralPath $Path))
    {
        return
    }

    $sidType = [System.Security.Principal.SecurityIdentifier]
    $trustedOwners = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    foreach ($sid in @('S-1-5-18', 'S-1-5-32-544', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464'))
    {
        [void]$trustedOwners.Add($sid)
    }
    [void]$trustedOwners.Add(([System.Security.Principal.WindowsIdentity]::GetCurrent()).User.Value)
    try
    {
        [void]$trustedOwners.Add((New-Object System.Security.Principal.NTAccount($ServiceAccount)).Translate($sidType).Value)
    }
    catch
    {
        # The service is not registered yet (fresh install), so it owns nothing here.
        Write-Verbose "Service account $ServiceAccount does not resolve yet: $_"
    }

    for ($pass = 1; $pass -le 20; $pass++)
    {
        $listErrors = $null
        $items = @(Get-Item -LiteralPath $Path -Force) + @(Get-ChildItem -LiteralPath $Path -Recurse -Force -ErrorAction SilentlyContinue -ErrorVariable listErrors)

        $links = @($items | Where-Object { $_.Attributes -band [System.IO.FileAttributes]::ReparsePoint } | ForEach-Object { $_.FullName })
        if ($links.Count -gt 0)
        {
            throw (Get-LinkRefusalMessage -Path $Path -Links $links -State 'The service has not been stopped, and nothing Collabhost created has been changed.')
        }

        $retake = New-Object System.Collections.Generic.List[string]
        foreach ($listError in @($listErrors))
        {
            $target = [string]$listError.TargetObject
            if (-not $target)
            {
                throw "Could not list part of ${Path}: $($listError.Exception.Message)"
            }
            $retake.Add($target)
        }

        foreach ($item in $items)
        {
            try
            {
                $acl = Get-Acl -LiteralPath $item.FullName
                $owner = $acl.GetOwner($sidType).Value
                $hasDeny = @($acl.GetAccessRules($true, $true, $sidType) | Where-Object { $_.AccessControlType -eq 'Deny' }).Count -gt 0
                # The root's inheritance is switched off by design once an install has run.
                $blocked = $acl.AreAccessRulesProtected -and ($item.FullName.TrimEnd('\') -ne $Path.TrimEnd('\'))
                if ($blocked -or $hasDeny -or -not $trustedOwners.Contains($owner))
                {
                    $retake.Add($item.FullName)
                }
            }
            catch
            {
                $retake.Add($item.FullName)
            }
        }

        if ($retake.Count -eq 0)
        {
            return
        }

        foreach ($itemPath in @($retake | Select-Object -Unique))
        {
            Write-Host "  Taking back $itemPath (its owner or permissions were not set by this installer)..."
            $takeownOutput = & takeown.exe /F $itemPath /A 2>&1
            if ($LASTEXITCODE -ne 0)
            {
                throw "Could not take ownership of '$itemPath' (takeown exit code $LASTEXITCODE). Nothing else has been changed and the service has not been stopped. Inspect and delete it, then re-run.`n$($takeownOutput -join [Environment]::NewLine)"
            }
            Invoke-IcaclsCommand -Arguments @($itemPath, '/reset', '/Q')
        }
    }

    throw "Items under $Path still could not be taken back after 20 passes. Nothing else has been changed and the service has not been stopped."
}

# Restrict the whole %ProgramData%\Collabhost tree to SYSTEM and Administrators.
# Runs on every install and upgrade, after Repair-CollabhostDataRoot and before
# any config is written, so the tree is never more open than this while the
# installer works in it. In this order:
#   1. Inheritance from %ProgramData% is broken at the root: SYSTEM and
#      Administrators get full control, inherited by everything below, and
#      BUILTIN\Users and CREATOR OWNER are no longer passed down. From here on
#      nobody else can create anything in the tree.
#   2. The tree is checked for links again, now that it can no longer change
#      underneath the check. The recursive steps below must not follow one.
#   3. Administrators become the owner of every item. An owner can always rewrite
#      an item's ACL, so a file or folder created earlier by someone else must not
#      keep its creator as owner.
#   4. Every item below the root is reset to inherit only, which drops explicit
#      ACEs and inheritance blocks left on individual files or folders.
# The service account's own access is added afterwards by
# Grant-CollabhostServiceAccess, once the service exists and its SID resolves.
# Precondition: the root already holds the layout directories (the /reset step
# addresses its children by wildcard).
function Protect-CollabhostDataRoot
{
    param
    (
        [Parameter(Mandatory = $true)][string]$Path
    )

    # Well-known SIDs, so the call does not depend on the Windows display language.
    $system         = '*S-1-5-18'
    $administrators = '*S-1-5-32-544'

    Invoke-IcaclsCommand -Arguments @($Path, '/inheritance:r', '/grant:r', "${system}:(OI)(CI)F", "${administrators}:(OI)(CI)F", '/Q')

    $links = Get-LinkInTree -Path $Path
    if ($links.Count -gt 0)
    {
        throw (Get-LinkRefusalMessage -Path $Path -Links $links -State 'The service is stopped; delete the link and re-run the installer to finish.')
    }

    Invoke-IcaclsCommand -Arguments @($Path, '/setowner', $administrators, '/T', '/Q')
    Invoke-IcaclsCommand -Arguments @((Join-Path $Path '*'), '/reset', '/T', '/Q')
}

# Give the service account exactly the access it needs under the protected root:
# Modify on the directories it writes at runtime, Read & Execute on config\ (it
# reads appsettings.json at startup and never writes config). Nothing on the root
# itself. Each grant is (OI)(CI) on the directory and reaches existing and future
# children by inheritance, which Protect-CollabhostDataRoot has just restored for
# every item. /grant:r replaces any earlier grant to the same account rather than
# adding a second one.
function Grant-CollabhostServiceAccess
{
    param
    (
        [Parameter(Mandatory = $true)][string]$Account,
        [Parameter(Mandatory = $true)][string[]]$WritableDirs,
        [Parameter(Mandatory = $true)][string[]]$ReadOnlyDirs
    )

    foreach ($dir in $WritableDirs)
    {
        Invoke-IcaclsCommand -Arguments @($dir, '/grant:r', "${Account}:(OI)(CI)M", '/Q')
    }

    foreach ($dir in $ReadOnlyDirs)
    {
        Invoke-IcaclsCommand -Arguments @($dir, '/grant:r', "${Account}:(OI)(CI)RX", '/Q')
    }
}

# Empty the single-file native-library extraction directory. The runtime
# re-extracts on the next start, so this only costs a few seconds, and it means
# nothing that was planted there while the directory was writable by other local
# users (older installs) survives into the new install. Run only while the
# service is stopped, and only after Protect-CollabhostDataRoot: by then the tree
# holds no links and every item is owned by Administrators and inherits their full
# control, so nothing a former planter left can block the delete or redirect it.
function Clear-CollabhostBundleCache
{
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '')]
    [CmdletBinding()]
    param
    (
        [Parameter(Mandatory = $true)][string]$Path
    )

    if (Test-Path -LiteralPath $Path)
    {
        Get-ChildItem -LiteralPath $Path -Force | Remove-Item -Recurse -Force
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

    # Remove the Application event-log source the installer pre-created (best
    # effort -- a lingering source is harmless, but cleaning it up matches the
    # rest of the uninstall).
    try
    {
        if ([System.Diagnostics.EventLog]::SourceExists($ServiceName))
        {
            Write-Host "Removing '$ServiceName' event-log source..."
            [System.Diagnostics.EventLog]::DeleteEventSource($ServiceName)
        }
    }
    catch
    {
        Write-Host "  Could not remove event-log source: $($_.Exception.Message)"
    }

    # Always remove %ProgramFiles%\Collabhost (binaries + wwwroot + docs).
    if (Test-Path -LiteralPath $InstallPrefix)
    {
        Write-Host "Removing $InstallPrefix..."
        Remove-Item -LiteralPath $InstallPrefix -Recurse -Force
    }

    # Config + logs always go. Caddy storage and the dotnet-bundle extract dir
    # always go (both re-issuable / re-extracted on next install). Removing these
    # directories also reaps the icacls grants made to the service account on
    # them -- the ACEs live on the directories, so deleting the directory removes
    # the grant with it.
    foreach ($removable in @($ConfigDir, $LogDir, $CaddyStorageDir, $UserTypesDir, $DotnetBundleDir))
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

    # The 'NT SERVICE\Collabhost' virtual account is auto-managed by the SCM and
    # needs no explicit deletion -- removing the service registration above
    # retires it. (Any icacls grant that survives on a preserved data/ directory
    # references the same service-name-derived SID and is re-validated if the
    # service is reinstalled.)
    Write-Host ''
    Write-Host "$ServiceName uninstalled."
    Write-Host '  Service registration + virtual account retired.'
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

# ---- Download policy --------------------------------------------------------

# Every download (the archive and checksums.txt) goes over HTTPS. A plain
# http:// base URL (COLLABHOST_INSTALL_BASE_URL) is accepted in exactly two
# cases, and each is announced on the console:
#
#   * The host is this machine (localhost, 127.x.y.z, [::1]). The request never
#     leaves the host, so nothing on a network can read or alter it.
#   * COLLABHOST_INSTALL_ALLOW_HTTP=1 is set. The operator accepts that the
#     archive and the checksum it is verified against both arrive unprotected,
#     so the checksum no longer proves anything.
#
# The rule is applied again to the address each download finally came from:
# Windows PowerShell 5.1 follows an https -> http redirect without complaint
# (PowerShell 7 refuses one), so a response that ended on plain HTTP from
# another host is rejected before it is used. The host test is System.Uri's
# own, the same parser the download connects with, so the address checked is
# the address connected to. Keep these two functions identical in install.ps1
# and install-system.ps1.
function Assert-DownloadUrl
{
    param
    (
        [Parameter(Mandatory = $true)]
        [string]$Label,
        [Parameter(Mandatory = $true)]
        [AllowEmptyString()]
        [string]$Url,
        [switch]$Quiet
    )

    $parsed = $null
    if (-not [uri]::TryCreate($Url, [UriKind]::Absolute, [ref]$parsed) -or ($parsed.Scheme -ne 'https' -and $parsed.Scheme -ne 'http'))
    {
        throw "Refusing ${Label}: '$Url' is not an https:// URL."
    }

    if ($parsed.Scheme -eq 'https')
    {
        return
    }

    if ($parsed.IsLoopback)
    {
        if (-not $Quiet)
        {
            Write-Host "Note: $Label uses plain HTTP to $($parsed.Host), which is this machine."
        }
        return
    }

    if ($env:COLLABHOST_INSTALL_ALLOW_HTTP -eq '1')
    {
        if (-not $Quiet)
        {
            Write-Warning "$Label uses plain HTTP to $($parsed.Host) (COLLABHOST_INSTALL_ALLOW_HTTP=1). The archive and its checksum both arrive unprotected: anyone on the network path can replace them."
        }
        return
    }

    throw "Refusing ${Label}: '$Url' is plain HTTP. Use an https:// URL; a mirror on this machine (http://localhost, http://127.0.0.1) is accepted. To download over plain HTTP from another host anyway, set COLLABHOST_INSTALL_ALLOW_HTTP=1."
}

function Assert-DownloadResponse
{
    param
    (
        [Parameter(Mandatory = $true)]
        $Response,
        [Parameter(Mandatory = $true)]
        [string]$RequestUrl,
        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    # The final address after any redirects: HttpWebResponse.ResponseUri on
    # Windows PowerShell 5.1, HttpResponseMessage.RequestMessage.RequestUri on
    # PowerShell 7.
    $final = $null
    $base = $Response.BaseResponse
    if ($null -ne $base -and $base.PSObject.Properties['ResponseUri'])
    {
        $final = $base.ResponseUri
    }
    elseif ($null -ne $base -and $base.PSObject.Properties['RequestMessage'] -and $base.RequestMessage)
    {
        $final = $base.RequestMessage.RequestUri
    }

    if ($null -eq $final)
    {
        throw "Could not tell where the $Label download came from; refusing to use it."
    }

    # An HTTPS request must end on HTTPS, wherever the redirect pointed.
    if (([uri]$RequestUrl).Scheme -eq 'https' -and $final.Scheme -ne 'https' -and $env:COLLABHOST_INSTALL_ALLOW_HTTP -ne '1')
    {
        throw "Refusing the $Label download: it was requested over HTTPS and redirected to $($final.AbsoluteUri). To accept a redirect to plain HTTP anyway, set COLLABHOST_INSTALL_ALLOW_HTTP=1."
    }

    Assert-DownloadUrl -Label "$Label download (after redirects)" -Url $final.AbsoluteUri -Quiet
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

# Accepts vX.Y.Z and SemVer 2.0 section 9 pre-release tags (e.g. v1.2.1-rc1, v2.0.0-beta.3).
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
Assert-DownloadUrl -Label 'COLLABHOST_INSTALL_BASE_URL' -Url $BaseUrl

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
    $archiveResponse = Invoke-WithRetry -Script {
        Invoke-WebRequest -UseBasicParsing -Uri $ArchiveUrl -OutFile $ArchivePath -PassThru
    }
    Assert-DownloadResponse -Response $archiveResponse -RequestUrl $ArchiveUrl -Label 'archive'

    Write-Host 'Downloading checksums.txt...'
    $checksumsResponse = Invoke-WithRetry -Script {
        Invoke-WebRequest -UseBasicParsing -Uri $ChecksumsUrl -OutFile $ChecksumsPath -PassThru
    }
    Assert-DownloadResponse -Response $checksumsResponse -RequestUrl $ChecksumsUrl -Label 'checksums.txt'

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

    # Before stopping anything: take back items in %ProgramData%\Collabhost that an
    # older install's open permissions let another local user create, and refuse to
    # go on if the tree contains a link. If this throws, the running install is
    # untouched (see Repair-CollabhostDataRoot).
    if (Test-Path -LiteralPath $DataRoot)
    {
        Write-Host "Checking $DataRoot for items this installer did not create..."
        Repair-CollabhostDataRoot -Path $DataRoot -ServiceAccount $ServiceAccount
    }

    # Anomaly F: Windows file-locking requires stop-before-binary-replace. If
    # the service is registered AND running, we MUST stop it before overwriting
    # %ProgramFiles%\Collabhost\bin\collabhost.exe -- the OS holds an exclusive
    # lock on the EXE for the lifetime of the running process and Copy-Item
    # would fail with "The process cannot access the file because it is being
    # used by another process."
    Stop-CollabhostServiceIfRunning

    # ---- Layout ------------------------------------------------------------

    foreach ($dir in @($InstallPrefix, $BinDir, $DataRoot, $ConfigDir, $DataDir, $UserTypesDir, $CaddyStorageDir, $LogDir, $DotnetBundleDir))
    {
        if (-not (Test-Path -LiteralPath $dir))
        {
            New-Item -ItemType Directory -Path $dir -Force | Out-Null
        }
    }

    # Lock %ProgramData%\Collabhost down before writing anything into it. On a
    # fresh install the tree is still empty; on an upgrade from an older script
    # this is what removes the BUILTIN\Users access that install was left with.
    # The service account is granted its own access after registration, below.
    Write-Host "Restricting $DataRoot to SYSTEM and Administrators..."
    Protect-CollabhostDataRoot -Path $DataRoot

    # On an upgrade the service already exists, so give it its access back now
    # rather than after the binary copy, merge and sc.exe steps below: if one of
    # those fails, the stopped service can still be started. (The same grant runs
    # again after registration; /grant:r makes that a no-op.)
    if (Get-CollabhostService)
    {
        Grant-CollabhostServiceAccess -Account $ServiceAccount -WritableDirs $WritableServiceDirs -ReadOnlyDirs $ReadOnlyServiceDirs
    }

    Clear-CollabhostBundleCache -Path $DotnetBundleDir

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
    # UAT runbook can compare against /api/v1/version.wwwrootHash.
    # Optional for archives predating the wwwroot-hash sidecar -- absence is silent.
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

    # Logon identity: the dedicated virtual account. sc.exe takes the account in
    # the 'obj=' token with NO 'password=' token at all -- virtual accounts carry
    # no password, and the SCM grants them the "log on as a service" right
    # automatically (no separate account creation or privilege grant needed).
    # Do NOT re-add `password= '""'`: PowerShell's single-quoted '""' is two
    # literal quote characters, not an empty string, so the SCM sees a 2-char
    # password and rejects the virtual account with exit 1057
    # (ERROR_INVALID_SERVICE_ACCOUNT) on some Windows editions (observed on
    # Windows 11 Home). The token-less form is the correct one for a virtual
    # account. On a re-run against a LocalSystem-era install this is the line
    # that migrates the service to the least-privilege account.
    if (Get-CollabhostService)
    {
        Write-Host "$ServiceName service already registered -- updating binary path + service account..."
        # New-Service has no Set-Service equivalent for BinaryPathName on PS 5.1
        # (Set-Service -BinaryPathName landed in PS 6.0). sc.exe config covers
        # both PS versions cleanly. Note: sc.exe's "key= value" syntax requires
        # a space AFTER the '=' (a leading space in PS argument parsing strips
        # the second token; `binPath= "..."` is two tokens, NOT one).
        & sc.exe config $ServiceName binPath= $ServiceBinaryPath start= delayed-auto obj= $ServiceAccount | Out-Null
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

        # Set the virtual-account logon identity, then the network-wait posture:
        # depend on Tcpip + delayed-auto so the service starts after the network
        # stack is up (mirrors the Linux unit's After/Wants=network-online.target).
        # sc.exe config writes are idempotent; running on every install is harmless.
        & sc.exe config $ServiceName obj= $ServiceAccount start= delayed-auto depend= Tcpip | Out-Null
        if ($LASTEXITCODE -ne 0)
        {
            throw "sc.exe config $ServiceName account/start/depend failed with exit code $LASTEXITCODE."
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

    # ---- Least-privilege filesystem grants ---------------------------------

    # Grant the virtual account Modify on each writable %ProgramData% directory
    # and read-only access on config/. The service account resolves only once the
    # service is registered (above), so this must run after registration and
    # before the service starts (it needs write access at first boot: SQLite,
    # crash logs, Caddy storage, and the single-file native-lib extract dir). The
    # service reads appsettings.json (Auth:AdminKey + the rest) at startup but
    # never writes config at runtime, so config/ gets Read, not Modify -- matching
    # the Linux install's read-only appsettings.json posture. %ProgramFiles% is
    # intentionally never granted -- it stays read-only to the account, which is
    # the whole point of moving off LocalSystem.
    Write-Host "Granting $ServiceAccount write access to data directories and read access to config..."
    Grant-CollabhostServiceAccess -Account $ServiceAccount -WritableDirs $WritableServiceDirs -ReadOnlyDirs $ReadOnlyServiceDirs

    # ---- Service-scoped environment variables ------------------------------

    # Point the binary at the canonical Windows system layout. Each path mirrors
    # what install-system.sh sets in the systemd unit's [Service] block (lines
    # 363-385 of that script) -- this is the Windows analog of the Linux
    # Environment="..." directives. The SCM reads these at service start and
    # injects them into the binary's process environment before ExecStart.
    #
    # The system-scope contract: when ASPNETCORE_CONTENTROOT is set, the binary
    # resolves ContentRoot to %ProgramFiles%\Collabhost so wwwroot/ resolves
    # correctly; COLLABHOST_CONFIG_PATH points the explicit
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
    # SCM honors this convention on Win10+/Server 2016+.
    $ServiceRegistryKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    #
    # DOTNET_BUNDLE_EXTRACT_BASE_DIR points the single-file host's native-library
    # self-extract at a directory the virtual account is granted to write. Under
    # LocalSystem the default extract location (a writable system temp) sufficed;
    # a limited virtual account cannot rely on that, so the writable location is
    # told to the runtime rather than discovered -- the Windows analog of the
    # Linux unit's DOTNET_BUNDLE_EXTRACT_BASE_DIR directive.
    $ServiceEnvVars = @(
        "ASPNETCORE_CONTENTROOT=$InstallPrefix",
        "COLLABHOST_CONFIG_PATH=$AppSettingsPath",
        "COLLABHOST_DATA_PATH=$DataDir",
        "COLLABHOST_USER_TYPES_PATH=$UserTypesDir",
        "COLLABHOST_LOGS_PATH=$LogDir",
        "COLLABHOST_PROXY_STORAGE_PATH=$CaddyStorageDir",
        "DOTNET_BUNDLE_EXTRACT_BASE_DIR=$DotnetBundleDir",
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

    # ---- Event Log source --------------------------------------------------

    # Pre-create the "Collabhost" Application-log event source from the elevated
    # installer. Under the Windows Service, UseWindowsService() registers the
    # Event Log logging provider with this source name, and the service's
    # warnings and errors surface there (the first-boot admin key does not: it is
    # saved to data\initial-admin-key.txt and never logged). Creating an event
    # source writes under HKLM\SYSTEM\CurrentControlSet\Services\EventLog and
    # needs administrator rights: LocalSystem could self-create it on first
    # write, but the limited virtual account cannot. Writing events to an
    # existing source is permitted for the account, so creating it here
    # (idempotently) is the fix. The source name must match Program.cs's
    # options.ServiceName.
    try
    {
        if (-not [System.Diagnostics.EventLog]::SourceExists($ServiceName))
        {
            Write-Host "Registering '$ServiceName' Application event-log source..."
            [System.Diagnostics.EventLog]::CreateEventSource($ServiceName, 'Application')
        }
    }
    catch
    {
        Write-Host "Warning: could not pre-create the '$ServiceName' event-log source -- $($_.Exception.Message)"
        Write-Host "  The service's warnings and errors may not reach the Application event log under the"
        Write-Host "  virtual account. If you cannot find the first-boot admin key in $DataDir\initial-admin-key.txt"
        Write-Host "  (or, on releases from before that file, in the event log), set Auth:AdminKey in"
        Write-Host "  $AppSettingsPath to a known value and restart the service (break-glass; see INSTALL.md section 2)."
    }

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
    Write-Host "  Account:     $ServiceAccount  (least-privilege virtual account)"
    Write-Host ''
    if ($IsReinstall)
    {
        Write-Host 'Reinstall: data and Caddy storage preserved; binaries + wwwroot refreshed.'
    }
    else
    {
        Write-Host 'First boot: collabhost saves its admin key once, to a file.'
        Write-Host '  Read it from an elevated PowerShell, then delete the file:'
        Write-Host "    Get-Content `"$DataDir\initial-admin-key.txt`""
        Write-Host '  No such file? Releases from before the key file logged it to the Application event log instead:'
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
