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
    .\install.ps1 -Version vX.Y.Z

.EXAMPLE
    # Pin via environment variable
    $env:COLLABHOST_VERSION = 'vX.Y.Z'; .\install.ps1
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
    [switch]$RegisterUserService,
    [switch]$UnregisterUserService,
    [pscredential]$ServiceCredential,
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
  -RegisterUserService  After installing, register a boot-start Windows service
                        ('CollabhostUser') that runs under your account. Requires
                        an elevated PowerShell. The service starts at boot with no
                        interactive login required (the installer grants your
                        account the "Log on as a service" right). Privileged ports
                        (80/443) are NOT granted -- the service stays on the
                        high-port default (58400). For the system-scope shape
                        (least-privilege virtual account, %ProgramFiles% layout),
                        use install-system.ps1 instead. See INSTALL.md section 5.5.
  -UnregisterUserService  Stop and remove the 'CollabhostUser' service and revoke
                        the "Log on as a service" right the installer granted.
                        Leaves your installed files and data in place. Requires an
                        elevated PowerShell.
  -ServiceCredential    PSCredential for the account the service runs as (used with
                        -RegisterUserService). Defaults to prompting for the current
                        user's password.
  -Help                 Print this message and exit

Environment:
  COLLABHOST_VERSION           Same as -Version
  COLLABHOST_INSTALL_BASE_URL  Override archive download base URL (default: GitHub Releases)
'@
    return
}

# ---- User-scope service support (opt-in via -RegisterUserService) -----------

# The user-scope service is a SEPARATE registration from the system-scope
# install-system.ps1 service ('Collabhost'). It runs the per-user binary in
# $HOME\.collabhost\bin under the operator's own account so it has the same
# filesystem access the foreground launch has -- the user-scope analog of
# systemd's `systemctl --user` + lingering. The distinct name avoids colliding
# with a system-scope install on the same box.
$UserServiceName        = 'CollabhostUser'
$UserServiceDisplayName = 'Collabhost (user-scope)'
$UserServiceDescription = 'Collabhost self-hosted application platform (user-scope service).'

# The single-file binary's Windows Event Log source. UseWindowsService() in the
# binary pins the source name to 'Collabhost' regardless of the SCM service name,
# so the first-boot admin key (emitted only via ILogger when running headless as a
# service) surfaces under this source. The installer pre-creates it because a
# limited account cannot create an event-log source on first write the way an
# elevated/SYSTEM context can; writing to an existing source is permitted. Shared
# with a system-scope install if one is present -- creation is idempotent.
$UserServiceEventSource = 'Collabhost'

# The privilege the account must hold to run a service with no interactive login.
$ServiceLogonRight      = 'SeServiceLogonRight'

function Test-IsElevated
{
    $identity  = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-CollabhostUserService
{
    return Get-Service -Name $UserServiceName -ErrorAction SilentlyContinue
}

# Stop the user-scope service if it is registered and running. Required before a
# reinstall overwrites collabhost.exe: Windows holds an exclusive lock on a
# running EXE, so Copy-Item would otherwise fail with "The process cannot access
# the file because it is being used by another process." Mirrors the
# stop-before-binary-replace step install-system.ps1 performs for the same reason.
function Stop-CollabhostUserServiceIfRunning
{
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '')]
    [CmdletBinding()]
    param
    (
        [int]$TimeoutSeconds = 30
    )

    $svc = Get-CollabhostUserService
    if (-not $svc -or $svc.Status -eq 'Stopped')
    {
        return
    }

    Write-Host "Stopping existing $UserServiceName service..."
    try
    {
        Stop-Service -Name $UserServiceName -Force -ErrorAction Stop
    }
    catch
    {
        Write-Host "  Stop-Service failed: $($_.Exception.Message). Falling through to wait + force-kill."
    }

    try
    {
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds($TimeoutSeconds))
    }
    catch [System.ServiceProcess.TimeoutException]
    {
        Write-Host "  Service did not stop within ${TimeoutSeconds}s. Attempting taskkill /F fallback..."
        $svcInfo = Get-CimInstance -ClassName Win32_Service -Filter "Name='$UserServiceName'" -ErrorAction SilentlyContinue
        if ($svcInfo -and $svcInfo.ProcessId -gt 0)
        {
            & taskkill.exe /F /PID $svcInfo.ProcessId 2>&1 | ForEach-Object { Write-Host "  $_" }
        }
        $svc.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(10))
    }
}

# Resolve a credential's account name to its SID (binary form) for the LSA right
# grant. Normalizes the '.\' local-machine prefix to the computer name so
# NTAccount resolves it; a bare username and DOMAIN\user / MACHINE\user pass through.
function Resolve-AccountSid
{
    param
    (
        [Parameter(Mandatory = $true)][string]$AccountName
    )

    $resolved = $AccountName
    if ($resolved.StartsWith('.\'))
    {
        $resolved = "$env:COMPUTERNAME\" + $resolved.Substring(2)
    }

    $sid      = (New-Object System.Security.Principal.NTAccount($resolved)).Translate([System.Security.Principal.SecurityIdentifier])
    $sidBytes = New-Object byte[] $sid.BinaryLength
    $sid.GetBinaryForm($sidBytes, 0)
    return , $sidBytes
}

# Local Security Authority (LSA) interop for user-rights assignments. There is no
# native PowerShell cmdlet for granting/revoking a privilege like
# SeServiceLogonRight, and secedit's export/import dance operates on the whole
# security policy (risky + fragile). LsaAddAccountRights / LsaRemoveAccountRights
# are the surgical, idempotent primitives for exactly one account + one right.
function Initialize-LsaInterop
{
    if (([System.Management.Automation.PSTypeName]'Collabhost.LsaUserRights').Type)
    {
        return
    }

    # Add-Type's -MemberDefinition wraps these in a class and injects the default
    # usings (System, System.Runtime.InteropServices), so no inline using directives.
    Add-Type -Namespace 'Collabhost' -Name 'LsaUserRights' -MemberDefinition @'
[StructLayout(LayoutKind.Sequential)]
struct LSA_UNICODE_STRING
{
    public ushort Length;
    public ushort MaximumLength;
    public IntPtr Buffer;
}

[StructLayout(LayoutKind.Sequential)]
struct LSA_OBJECT_ATTRIBUTES
{
    public int Length;
    public IntPtr RootDirectory;
    public IntPtr ObjectName;
    public uint Attributes;
    public IntPtr SecurityDescriptor;
    public IntPtr SecurityQualityOfService;
}

[DllImport("advapi32.dll", SetLastError = true)]
static extern uint LsaOpenPolicy(IntPtr SystemName, ref LSA_OBJECT_ATTRIBUTES ObjectAttributes, uint AccessMask, out IntPtr PolicyHandle);

[DllImport("advapi32.dll", SetLastError = true)]
static extern uint LsaAddAccountRights(IntPtr PolicyHandle, byte[] AccountSid, LSA_UNICODE_STRING[] UserRights, uint CountOfRights);

[DllImport("advapi32.dll", SetLastError = true)]
static extern uint LsaRemoveAccountRights(IntPtr PolicyHandle, byte[] AccountSid, bool AllRights, LSA_UNICODE_STRING[] UserRights, uint CountOfRights);

[DllImport("advapi32.dll", SetLastError = true)]
static extern uint LsaEnumerateAccountRights(IntPtr PolicyHandle, byte[] AccountSid, out IntPtr UserRights, out uint CountOfRights);

[DllImport("advapi32.dll")]
static extern uint LsaClose(IntPtr PolicyHandle);

[DllImport("advapi32.dll")]
static extern int LsaNtStatusToWinError(uint Status);

[DllImport("advapi32.dll")]
static extern uint LsaFreeMemory(IntPtr Buffer);

const uint POLICY_LOOKUP_NAMES = 0x00000800;
const uint POLICY_CREATE_ACCOUNT = 0x00000010;
const uint POLICY_VIEW_LOCAL_INFORMATION = 0x00000001;
const uint STATUS_OBJECT_NAME_NOT_FOUND = 0xC0000034;

static IntPtr OpenPolicy(uint access)
{
    LSA_OBJECT_ATTRIBUTES attrs = new LSA_OBJECT_ATTRIBUTES();
    IntPtr handle;
    uint status = LsaOpenPolicy(IntPtr.Zero, ref attrs, access, out handle);
    if (status != 0)
    {
        throw new System.ComponentModel.Win32Exception(LsaNtStatusToWinError(status), "LsaOpenPolicy failed.");
    }
    return handle;
}

static LSA_UNICODE_STRING[] RightArray(string right)
{
    LSA_UNICODE_STRING s = new LSA_UNICODE_STRING();
    s.Buffer = Marshal.StringToHGlobalUni(right);
    s.Length = (ushort)(right.Length * 2);
    s.MaximumLength = (ushort)(right.Length * 2 + 2);
    return new LSA_UNICODE_STRING[] { s };
}

public static void Grant(byte[] sid, string right)
{
    IntPtr policy = OpenPolicy(POLICY_CREATE_ACCOUNT | POLICY_LOOKUP_NAMES);
    try
    {
        LSA_UNICODE_STRING[] rights = RightArray(right);
        try
        {
            uint status = LsaAddAccountRights(policy, sid, rights, 1);
            if (status != 0)
            {
                throw new System.ComponentModel.Win32Exception(LsaNtStatusToWinError(status), "LsaAddAccountRights failed.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(rights[0].Buffer);
        }
    }
    finally
    {
        LsaClose(policy);
    }
}

public static void Revoke(byte[] sid, string right)
{
    IntPtr policy = OpenPolicy(POLICY_CREATE_ACCOUNT | POLICY_LOOKUP_NAMES);
    try
    {
        LSA_UNICODE_STRING[] rights = RightArray(right);
        try
        {
            uint status = LsaRemoveAccountRights(policy, sid, false, rights, 1);
            // STATUS_OBJECT_NAME_NOT_FOUND => the account had no rights at all; nothing to revoke.
            if (status != 0 && status != STATUS_OBJECT_NAME_NOT_FOUND)
            {
                throw new System.ComponentModel.Win32Exception(LsaNtStatusToWinError(status), "LsaRemoveAccountRights failed.");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(rights[0].Buffer);
        }
    }
    finally
    {
        LsaClose(policy);
    }
}

public static bool Has(byte[] sid, string right)
{
    IntPtr policy = OpenPolicy(POLICY_VIEW_LOCAL_INFORMATION | POLICY_LOOKUP_NAMES);
    try
    {
        IntPtr rightsPtr;
        uint count;
        uint status = LsaEnumerateAccountRights(policy, sid, out rightsPtr, out count);
        if (status == STATUS_OBJECT_NAME_NOT_FOUND)
        {
            return false;
        }
        if (status != 0)
        {
            throw new System.ComponentModel.Win32Exception(LsaNtStatusToWinError(status), "LsaEnumerateAccountRights failed.");
        }
        try
        {
            IntPtr cursor = rightsPtr;
            int structSize = Marshal.SizeOf(typeof(LSA_UNICODE_STRING));
            for (uint i = 0; i < count; i++)
            {
                LSA_UNICODE_STRING s = (LSA_UNICODE_STRING)Marshal.PtrToStructure(cursor, typeof(LSA_UNICODE_STRING));
                string name = Marshal.PtrToStringUni(s.Buffer, s.Length / 2);
                if (string.Equals(name, right, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                cursor = (IntPtr)(cursor.ToInt64() + structSize);
            }
            return false;
        }
        finally
        {
            LsaFreeMemory(rightsPtr);
        }
    }
    finally
    {
        LsaClose(policy);
    }
}
'@
}

# Register (or update) the user-scope boot-start service. Runs AFTER the install
# body has laid the binary down, so collabhost.exe exists at its final path.
function Register-CollabhostUserService
{
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '')]
    [CmdletBinding()]
    param
    (
        [Parameter(Mandatory = $true)][string]$BinDir,
        [pscredential]$Credential
    )

    # Resolve the credential. Default to the current user; prompt for the password
    # (the SCM needs it to start a named-account service at boot). The prompt is a
    # host-UI call, so it works even though install.ps1 is documented as iwr-piped
    # -- but -RegisterUserService is realistically run from a downloaded script.
    if (-not $Credential)
    {
        $defaultUser = "$env:USERDOMAIN\$env:USERNAME"
        $Credential  = Get-Credential -UserName $defaultUser -Message "Password for $defaultUser -- the account the CollabhostUser boot service will run as."
    }
    if (-not $Credential)
    {
        throw 'A credential is required to register the user-scope service. Re-run with -ServiceCredential or supply the password when prompted.'
    }

    $accountName = $Credential.UserName
    $sidBytes    = Resolve-AccountSid -AccountName $accountName

    # Grant "Log on as a service". Track whether the account already held it so
    # -UnregisterUserService only revokes a right THIS installer added -- revoking
    # a right another service depends on would break that service. The decision is
    # persisted as a registry marker under the service key (read back at uninstall,
    # since register/unregister are separate invocations). The marker is sticky
    # across reinstalls: once we record "we granted it," a later reinstall that
    # finds the right already present (because we granted it last time) preserves
    # the record rather than downgrading it to "pre-existing."
    $serviceRegistryKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$UserServiceName"
    $existingGrantMarker = 0
    if (Get-CollabhostUserService)
    {
        # An in-place upgrade does NOT re-supply the logon credential (the SCM
        # persists it; see the sc.exe config call below), so silently switching the
        # account would grant the new account the logon right while leaving the
        # service running as the old one -- a silently-ignored override. Detect a
        # genuine account change (compared by SID, so .\user / COMPUTERNAME\user /
        # DOMAIN\user forms compare equal) and fail loud BEFORE granting anything.
        $registeredAccount = (Get-CimInstance -ClassName Win32_Service -Filter "Name='$UserServiceName'" -ErrorAction SilentlyContinue).StartName
        if ($registeredAccount)
        {
            $registeredSid = $null
            try { $registeredSid = Resolve-AccountSid -AccountName $registeredAccount } catch { $registeredSid = $null }
            if ($registeredSid -and (($registeredSid -join ',') -ne ($sidBytes -join ',')))
            {
                throw "$UserServiceName already runs as '$registeredAccount'. Changing the service account on an in-place upgrade is not supported -- run 'install.ps1 -UnregisterUserService' first, then re-run -RegisterUserService with the new -ServiceCredential."
            }
        }

        try
        {
            $existingGrantMarker = [int](Get-ItemProperty -LiteralPath $serviceRegistryKey -Name 'CollabhostGrantedLogonRight' -ErrorAction Stop).CollabhostGrantedLogonRight
        }
        catch
        {
            $existingGrantMarker = 0
        }
    }

    Initialize-LsaInterop
    $alreadyHadRight = [Collabhost.LsaUserRights]::Has($sidBytes, $ServiceLogonRight)
    if (-not $alreadyHadRight)
    {
        Write-Host "Granting '$accountName' the 'Log on as a service' right..."
        [Collabhost.LsaUserRights]::Grant($sidBytes, $ServiceLogonRight)
    }
    else
    {
        Write-Host "'$accountName' already holds the 'Log on as a service' right."
    }

    # We are responsible for revoking the right at uninstall iff we added it now OR
    # a prior run of this installer already recorded that it added it.
    $weOwnTheGrant = (-not $alreadyHadRight) -or ($existingGrantMarker -eq 1)

    $serviceBinary = Join-Path $BinDir 'collabhost.exe'
    $binaryPathArg = '"' + $serviceBinary + '"'

    # Single-file native-library self-extract dir. A service starts with no loaded
    # user profile, so the runtime's default extract location (%TEMP%) may not be
    # writable by the account. Point DOTNET_BUNDLE_EXTRACT_BASE_DIR at an account-
    # owned directory under the install -- the writable location is told, not
    # discovered (mirrors install-system.ps1 + the Linux unit's dotnet-bundle dir).
    $dotnetBundleDir = Join-Path $BinDir 'dotnet-bundle'
    if (-not (Test-Path -LiteralPath $dotnetBundleDir))
    {
        New-Item -ItemType Directory -Path $dotnetBundleDir -Force | Out-Null
    }

    if (Get-CollabhostUserService)
    {
        Write-Host "$UserServiceName service already registered -- updating binary path (logon credential preserved by the SCM)..."
        # Set-Service -BinaryPathName landed in PS 6.0; sc.exe config covers PS 5.1
        # too. sc.exe's "key= value" syntax needs the space AFTER '=' (two tokens,
        # not one). The logon account + password are deliberately NOT re-supplied
        # here: the SCM already holds them from the initial registration and carries
        # them across a binPath change, so a version-only upgrade has no reason to
        # re-set them. Passing the password to sc.exe would put the operator secret
        # on the process command line (readable via Win32_Process.CommandLine for
        # the life of the call); a genuine account change was rejected above instead.
        & sc.exe config $UserServiceName binPath= $binaryPathArg start= delayed-auto | Out-Null
        if ($LASTEXITCODE -ne 0)
        {
            throw "sc.exe config $UserServiceName failed with exit code $LASTEXITCODE."
        }
    }
    else
    {
        Write-Host "Registering $UserServiceName service (boot-start, runs as '$accountName')..."
        New-Service `
            -Name           $UserServiceName `
            -DisplayName    $UserServiceDisplayName `
            -Description    $UserServiceDescription `
            -BinaryPathName $binaryPathArg `
            -StartupType    Automatic `
            -Credential     $Credential | Out-Null

        # Delayed-auto + Tcpip dependency: start after the network stack is up,
        # mirroring the Linux unit's After/Wants=network-online.target and the
        # system-scope install. Idempotent on re-run.
        & sc.exe config $UserServiceName start= delayed-auto depend= Tcpip | Out-Null
        if ($LASTEXITCODE -ne 0)
        {
            throw "sc.exe config $UserServiceName start/depend failed with exit code $LASTEXITCODE."
        }
    }

    # Crash-recovery: restart on the first + second failure (5s delay), no action
    # on the third so a crash-loop doesn't fight the operator. Reset the counter
    # after 1 day. Mirrors install-system.ps1.
    & sc.exe failure $UserServiceName reset= 86400 actions= restart/5000/restart/5000// | Out-Null
    if ($LASTEXITCODE -ne 0)
    {
        throw "sc.exe failure $UserServiceName failed with exit code $LASTEXITCODE."
    }

    # Service-scoped environment. No ASPNETCORE_CONTENTROOT / COLLABHOST_*_PATH
    # needed: the user-scope layout keeps the binary, wwwroot/, appsettings.json,
    # and data/ together under $BinDir, and the binary resolves them from
    # AppContext.BaseDirectory (the exe's own directory) regardless of the SCM's
    # working directory. Production is pinned to guard against a machine-scoped
    # Development override leaking into the boot service.
    # $serviceRegistryKey was resolved above (grant section). The service key now
    # exists (New-Service / sc.exe config), so both writes land.
    $serviceEnvVars = @(
        "DOTNET_BUNDLE_EXTRACT_BASE_DIR=$dotnetBundleDir",
        'ASPNETCORE_ENVIRONMENT=Production',
        'DOTNET_ENVIRONMENT=Production'
    )
    Set-ItemProperty -LiteralPath $serviceRegistryKey -Name 'Environment' -Value $serviceEnvVars -Type MultiString -ErrorAction Stop

    # Persist who owns the logon-right grant so -UnregisterUserService can revoke
    # exactly what this installer added (DWORD 1 = revoke at uninstall; 0 = leave).
    Set-ItemProperty -LiteralPath $serviceRegistryKey -Name 'CollabhostGrantedLogonRight' -Value ([int]$weOwnTheGrant) -Type DWord -ErrorAction Stop

    # Pre-create the Event Log source so the first-boot admin key surfaces. The
    # service runs headless (no console), so the key emits only via ILogger ->
    # Windows Event Log under the 'Collabhost' source.
    try
    {
        if (-not [System.Diagnostics.EventLog]::SourceExists($UserServiceEventSource))
        {
            Write-Host "Registering '$UserServiceEventSource' Application event-log source..."
            [System.Diagnostics.EventLog]::CreateEventSource($UserServiceEventSource, 'Application')
        }
    }
    catch
    {
        Write-Host "Warning: could not pre-create the '$UserServiceEventSource' event-log source -- $($_.Exception.Message)"
        Write-Host "  The first-boot admin key may not surface in the Application event log. If you cannot"
        Write-Host "  retrieve it, set Auth:AdminKey in $InstallPath\appsettings.json to a known value and"
        Write-Host "  restart the service (break-glass; see INSTALL.md section 2)."
    }

    Write-Host "Starting $UserServiceName..."
    try
    {
        Start-Service -Name $UserServiceName -ErrorAction Stop
    }
    catch
    {
        Write-Host "Warning: Start-Service $UserServiceName failed -- $($_.Exception.Message)"
        Write-Host "  A logon failure (error 1069/1326) usually means a wrong password or a missing"
        Write-Host "  'Log on as a service' right. Verify the credential and re-run -RegisterUserService."
        throw
    }

    Write-Host ''
    Write-Host "Registered $UserServiceName as a boot-start Windows service."
    Write-Host "  Account:  $accountName  (runs at boot, no interactive login required)"
    Write-Host "  Startup:  Automatic (Delayed Start)"
    Write-Host "  Binary:   $serviceBinary"
    Write-Host "  Service:  $UserServiceName  (Get-Service $UserServiceName)"
    Write-Host ''
    Write-Host 'Verify with:'
    Write-Host "  Get-Service $UserServiceName"
    Write-Host '  curl http://localhost:58400/api/v1/status'
    Write-Host ''
    Write-Host 'First boot prints the admin key once -- under the service it surfaces in the Application event log:'
    Write-Host '  Get-WinEvent -LogName Application -MaxEvents 200 |'
    Write-Host "    Where-Object { `$_.ProviderName -like 'collabhost*' -and `$_.Message -match 'Collabhost admin key:' } |"
    Write-Host '    Select-Object -First 1 -ExpandProperty Message'
    Write-Host ''
    Write-Host "Unregister with: install.ps1 -UnregisterUserService"
}

# Remove the user-scope service and revoke the logon right (only if this installer
# granted it). Leaves the installed files + data in place -- unregistering the
# service is distinct from removing the install.
function Unregister-CollabhostUserService
{
    [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseShouldProcessForStateChangingFunctions', '')]
    [CmdletBinding()]
    param()

    $svc = Get-CollabhostUserService
    if (-not $svc)
    {
        Write-Host "$UserServiceName service is not registered -- nothing to remove."
        return
    }

    # Resolve the logon account AND the grant-ownership marker from the registered
    # service BEFORE deleting it (Remove-Service deletes the service key with the
    # marker on it). Revoke only what this installer recorded that it granted.
    $svcInfo     = Get-CimInstance -ClassName Win32_Service -Filter "Name='$UserServiceName'" -ErrorAction SilentlyContinue
    $accountName = if ($svcInfo) { $svcInfo.StartName } else { $null }

    $weOwnTheGrant = $false
    try
    {
        $serviceRegistryKey = "HKLM:\SYSTEM\CurrentControlSet\Services\$UserServiceName"
        $weOwnTheGrant = ([int](Get-ItemProperty -LiteralPath $serviceRegistryKey -Name 'CollabhostGrantedLogonRight' -ErrorAction Stop).CollabhostGrantedLogonRight) -eq 1
    }
    catch
    {
        # No marker (older/partial install). Conservative: do not revoke -- a lingering
        # logon right is harmless, but revoking one another service needs is not.
        $weOwnTheGrant = $false
    }

    Stop-CollabhostUserServiceIfRunning

    Write-Host "Removing $UserServiceName service registration..."
    if (Get-Command Remove-Service -ErrorAction SilentlyContinue)
    {
        Remove-Service -Name $UserServiceName -ErrorAction Stop
    }
    else
    {
        & sc.exe delete $UserServiceName | Out-Null
        if ($LASTEXITCODE -ne 0)
        {
            throw "sc.exe delete $UserServiceName failed with exit code $LASTEXITCODE."
        }
    }

    # Revoke "Log on as a service" only if the marker says this installer added it.
    # LocalSystem/LocalService/NetworkService are built-in principals that never
    # need the grant -- skip them outright as a second guard.
    $builtInAccounts = @('LocalSystem', 'NT AUTHORITY\LocalService', 'NT AUTHORITY\NetworkService', 'NT AUTHORITY\SYSTEM')
    if ($weOwnTheGrant -and $accountName -and ($builtInAccounts -notcontains $accountName))
    {
        try
        {
            $sidBytes = Resolve-AccountSid -AccountName $accountName
            Initialize-LsaInterop
            Write-Host "Revoking the 'Log on as a service' right from '$accountName'..."
            [Collabhost.LsaUserRights]::Revoke($sidBytes, $ServiceLogonRight)
        }
        catch
        {
            Write-Host "  Could not revoke the logon right from '$accountName': $($_.Exception.Message)"
            Write-Host "  Revoke it by hand via secpol.msc if needed (User Rights Assignment -> Log on as a service)."
        }
    }

    # The 'Collabhost' event-log source is intentionally left in place: it is
    # shared with a system-scope install if one exists, and a lingering source is
    # harmless. Removing it would risk breaking a co-installed system-scope service.

    Write-Host ''
    Write-Host "$UserServiceName uninstalled. Your installed files and data are untouched."
    Write-Host "  To remove the install too: delete $InstallPath and remove it from your User PATH."
}

# ---- Service switch gate ----------------------------------------------------

$WantsServiceAction = $RegisterUserService -or $UnregisterUserService

if ($WantsServiceAction)
{
    # The service switches register a machine-level Windows service (HKLM) and
    # grant an LSA right -- both need elevation. The plain install (no switch)
    # stays non-elevated and writes only under $HOME. Mirrored from
    # install-system.ps1: hard-fail rather than UAC-relaunch, which does not
    # compose with the iwr-pipe-to-iex flow.
    $runningOnWindows = $IsWindows -or $PSVersionTable.PSEdition -eq 'Desktop'
    if (-not $runningOnWindows)
    {
        [Console]::Error.WriteLine('-RegisterUserService / -UnregisterUserService are Windows-only.')
        exit 1
    }
    if (-not (Test-IsElevated))
    {
        [Console]::Error.WriteLine('-RegisterUserService / -UnregisterUserService require an administrator-elevated PowerShell.')
        [Console]::Error.WriteLine('  Right-click PowerShell -> Run as administrator')
        [Console]::Error.WriteLine('  Or: Start-Process powershell -Verb RunAs')
        [Console]::Error.WriteLine('Then re-run the command.')
        exit 1
    }
}

# -UnregisterUserService is a teardown-only path -- no download/install. Handle it
# before any network work and return.
if ($UnregisterUserService)
{
    Unregister-CollabhostUserService
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

# Accepts vX.Y.Z and SemVer 2.0 §9 pre-release tags (e.g. v1.2.1-rc1, v2.0.0-beta.3).
# Build metadata (+...) is intentionally rejected -- archive filenames use the version
# as a path segment and '+' is friction across tools. Keep this pattern in sync with
# publish.yml, install-integration.yml, install-lib.sh, and install-system.ps1.
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

    # The archive is flat -- eight items sit at the archive root (seven files/dirs
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

    # If -RegisterUserService and the service is already registered + running, stop
    # it before overwriting collabhost.exe -- Windows holds an exclusive lock on a
    # running EXE and Copy-Item would otherwise fail. Re-registered/started below.
    if ($RegisterUserService)
    {
        Stop-CollabhostUserServiceIfRunning
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

    # wwwroot: always overwrite from the archive. This is the Portal SPA bundle
    # and must track the binary version exactly. Operators do not edit it; new
    # versions ship new bundles (see INSTALL.md §8 "Overwritten on re-run").
    $WwwrootDst = Join-Path $InstallPath 'wwwroot'
    if (Test-Path -LiteralPath $WwwrootDst)
    {
        Remove-Item -LiteralPath $WwwrootDst -Recurse -Force
    }
    Copy-Item -LiteralPath (Join-Path $ExtractDir 'wwwroot') -Destination $InstallPath -Recurse -Force

    # wwwroot.sha256 sidecar: build-time SHA-256 hash of the wwwroot/ tree,
    # written by the publish workflow. Sits next to the binary so the UAT
    # runbook can compare against /api/v1/version.wwwrootHash (#342). Optional
    # for archives predating #342 -- absence is silent.
    $WwwrootSidecarSrc = Join-Path $ExtractDir 'wwwroot.sha256'
    if (Test-Path -LiteralPath $WwwrootSidecarSrc)
    {
        Copy-Item -LiteralPath $WwwrootSidecarSrc -Destination $InstallPath -Force
    }

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
        # Match "Collabhost X.Y.Z" or "Collabhost vX.Y.Z" -- the optional 'v' guards
        # against any future change to VersionInfo.Current's prefix without re-breaking
        # the gate (card #213 root cause: the prior regex required 'v' that the binary
        # doesn't emit). Drop the major-version >= 1 constraint -- merge-appsettings
        # shipped in v1.0.0, so a v0.x binary won't recognize it and will exit non-zero,
        # which the gate already handles. Surface a warning when the regex misses so
        # the next format drift is loud instead of silent.
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
            # Non-fatal -- treat as unsupported and skip the merge.
            $probeFailed = $true
            Write-Verbose "Could not probe collabhost --version: $_"
        }

        if (-not $supportsMerge -and -not $probeFailed)
        {
            $observed = if ($versionLine) { $versionLine } else { '<empty>' }
            [Console]::Error.WriteLine("Warning: skipping appsettings.json smart-merge -- collabhost --version output did not match the expected pattern.")
            [Console]::Error.WriteLine("  Got:      $observed")
            [Console]::Error.WriteLine("  Expected: pattern '$versionPattern'")
            [Console]::Error.WriteLine("  Effect:   new shipped keys in appsettings.json may not be picked up automatically.")
            [Console]::Error.WriteLine("  See $AppSettingsDst and $ShippedSrc to reconcile by hand.")
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
        # COLLABHOST_PROXY_BINARY_PATH or fix the file by hand. Surface the failure so they
        # know to investigate.
        Write-Host "Warning: could not seed Proxy:BinaryPath in appsettings.json -- $($_.Exception.Message)"
        Write-Host "Set COLLABHOST_PROXY_BINARY_PATH to '$BundledCaddyPath' or repair the file by hand."
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
    Write-Host "After registering apps, run 'Start-Process collabhost -ArgumentList ''--update-hosts'' -Verb RunAs' so <slug>.collab.internal resolves from this host. See INSTALL.md section 9.10.2."
    Write-Host "See $InstallPath\INSTALL.md for configuration, env-var overrides, and upgrade notes."

    # ---- Optional: register the boot-start user-scope service ----------------

    # Runs only with -RegisterUserService, after the install body has laid the
    # binary at $InstallPath\collabhost.exe. Elevation was already enforced by the
    # service-switch gate above. The plain install path (no switch) is untouched.
    if ($RegisterUserService)
    {
        Write-Host ''
        Register-CollabhostUserService -BinDir $InstallPath -Credential $ServiceCredential
    }
}
finally
{
    if (Test-Path -LiteralPath $TmpDir)
    {
        Remove-Item -LiteralPath $TmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
