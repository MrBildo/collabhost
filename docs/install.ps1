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
    [switch]$VirtualAccount,
    [switch]$RunAsCurrentUser,
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
                        ('CollabhostUser'). By default the service runs under a
                        passwordless, machine-managed virtual account -- no password
                        is prompted or required, and it survives reboot with no
                        interactive login. (To run under a real account instead, see
                        -ServiceCredential and -RunAsCurrentUser.) Requires an
                        elevated PowerShell. The dashboard stays on loopback 58400;
                        the bundled Caddy front door binds 80/443 (Windows does not
                        gate low ports by account), so this shape and the system-scope
                        shape cannot co-reside on one host. For the system-scope shape
                        (%ProgramFiles% layout), use install-system.ps1 instead. See
                        INSTALL.md section 5.5.
  -UnregisterUserService  Stop and remove the 'CollabhostUser' service and revoke the
                        "Log on as a service" right if the installer granted one (the
                        default virtual-account install grants none). Leaves your
                        installed files and data in place. Requires an elevated
                        PowerShell.
  -ServiceCredential    PSCredential for a specific account the service runs as -- a
                        dedicated service account or your own (opt-in, used with
                        -RegisterUserService). The account is granted scoped access to
                        the install tree automatically, so a dedicated account is
                        turnkey (no manual icacls step). Not usable for a Microsoft-
                        account / PIN sign-in with no local password -- use the default
                        (virtual account) instead.
  -RunAsCurrentUser     Opt in to running the service under your own signed-in account
                        (used with -RegisterUserService). Prompts for your account
                        password (the SCM needs it to start the service at boot). Choose
                        this when the service should carry your identity and ambient file
                        access. Requires a usable password.
  -VirtualAccount       Explicitly select the default passwordless machine-managed
                        virtual account 'NT SERVICE\CollabhostUser' (same as
                        -RegisterUserService with no account flag). No credential is
                        prompted or required; the service gets scoped access to the
                        install tree. See INSTALL.md section 5.5.5.
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

# The passwordless service principal for -VirtualAccount: the per-service,
# SCM-managed virtual account derived from the service name. Carries no password
# and is auto-granted the logon-as-service right, so an operator with no usable
# password (Microsoft-account + PIN sign-in) can still register a boot service.
# Same principal shape the system-scope install (install-system.ps1) uses.
$UserServiceAccount     = "NT SERVICE\$UserServiceName"

# The single-file binary's Windows Event Log source. UseWindowsService() in the
# binary pins the source name to 'Collabhost' regardless of the SCM service name,
# so the service's warnings and errors surface under this source. (The first-boot
# admin key does not: it is saved to data\initial-admin-key.txt and never logged.)
# The installer pre-creates the source because a limited account cannot create an
# event-log source on first write the way an elevated/SYSTEM context can; writing
# to an existing source is permitted. Shared with a system-scope install if one is
# present -- creation is idempotent.
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

# Normalize a service-account name to the 'MACHINE\name' form that BOTH New-Service and
# icacls accept. Get-Credential and -ServiceCredential can hand back a bare name
# ('collabhost-svc') or the '.\name' local form, and the two APIs disagree on what they
# take: New-Service rejects a bare name (exit 1057, "account name is invalid or does not
# exist"), while icacls cannot map the '.\name' form (exit 1332, ERROR_NONE_MAPPED). Only
# the machine-qualified 'MACHINE\name' satisfies both. A name that already carries a
# domain/machine qualifier (DOMAIN\user, MACHINE\user) or is a UPN (user@domain) is a
# deliberate, already-qualified choice and passes through unchanged -- so a domain service
# account stays domain-qualified; only a bare SAMAccountName or a '.\' local name is treated
# as a local account and qualified with this computer's name (mirrors the '.\' handling in
# Resolve-AccountSid / Test-IsInstallerIdentity).
function Get-QualifiedAccountName
{
    param
    (
        [Parameter(Mandatory = $true)][string]$AccountName
    )

    if ($AccountName.StartsWith('.\'))
    {
        return "$env:COMPUTERNAME\" + $AccountName.Substring(2)
    }
    if (-not $AccountName.Contains('\') -and -not $AccountName.Contains('@'))
    {
        return "$env:COMPUTERNAME\$AccountName"
    }
    return $AccountName
}

# Compare a registered service StartName to a desired account for the account-change
# guard. Human accounts vary by prefix (.\user / MACHINE\user / DOMAIN\user) so they
# compare by SID; virtual accounts (NT SERVICE\*) and built-in principals have a
# single canonical form and compare as strings -- their SID does not resolve via
# NTAccount until the owning service exists, so a SID compare is unreliable for them.
function Test-SameServiceAccount
{
    param
    (
        [Parameter(Mandatory = $true)][string]$Registered,
        [Parameter(Mandatory = $true)][string]$Desired
    )

    if ($Registered -like 'NT SERVICE\*' -or $Desired -like 'NT SERVICE\*')
    {
        return $Registered -ieq $Desired
    }

    try
    {
        $registeredSid = Resolve-AccountSid -AccountName $Registered
        $desiredSid    = Resolve-AccountSid -AccountName $Desired
        return ($registeredSid -join ',') -eq ($desiredSid -join ',')
    }
    catch
    {
        return $Registered -ieq $Desired
    }
}

# True when the given account is the identity running this installer -- i.e. the account
# that owns the freshly-created install tree ($BinDir is created by New-Item as the caller,
# and a per-user profile tree grants its owner full control by inheritance). Such an account
# already has the filesystem access the service needs, so it is skipped for the icacls grant;
# any OTHER principal (the virtual account, or a dedicated named service account) does not own
# the tree and must be granted scoped access. On any resolution failure, return $false so the
# grant runs -- a redundant grant to an account that already has access is harmless, whereas
# withholding a grant an account genuinely needs breaks its start.
function Test-IsInstallerIdentity
{
    param
    (
        [Parameter(Mandatory = $true)][string]$AccountName
    )

    try
    {
        $currentSid = ([System.Security.Principal.WindowsIdentity]::GetCurrent()).User

        $resolved = $AccountName
        if ($resolved.StartsWith('.\'))
        {
            $resolved = "$env:COMPUTERNAME\" + $resolved.Substring(2)
        }
        $accountSid = (New-Object System.Security.Principal.NTAccount($resolved)).Translate([System.Security.Principal.SecurityIdentifier])

        return $accountSid.Equals($currentSid)
    }
    catch
    {
        return $false
    }
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
        [pscredential]$Credential,
        [switch]$VirtualAccount
    )

    # Three service-principal models (the caller resolves which via -VirtualAccount /
    # -Credential; the passwordless virtual account is the default):
    #  * Virtual account (default, or explicit -VirtualAccount): the service runs as the
    #    per-service, SCM-managed principal NT SERVICE\CollabhostUser. It carries NO
    #    password and is auto-granted the logon-as-service right, so it is the right
    #    default -- it works for an operator with no usable password (the common
    #    Microsoft-account + PIN sign-in, where the MSA password is unknown to the
    #    operator) and puts no secret on the box. It is given scoped access to the install
    #    tree via an explicit icacls grant. Mirrors the least-privilege virtual account
    #    install-system.ps1 uses.
    #  * A real account (opt-in): the service runs as a named account -- either the
    #    operator's own (-RunAsCurrentUser prompts for the operator's password) or a
    #    specific/dedicated account (-Credential, from -ServiceCredential). Needs the
    #    account password -- the SCM stores it as an LSA secret to log the account on at
    #    boot. A dedicated (non-operator) account does not own the install tree, so it is
    #    given the same scoped icacls grant the virtual account gets; the operator's own
    #    account already owns the tree (systemd --user parity) and needs none.
    if ($VirtualAccount)
    {
        $accountName = $UserServiceAccount

        # The virtual account is not the operator and inherits no access to the install
        # tree -- it always needs the scoped icacls grant.
        $needsFilesystemGrant = $true
    }
    else
    {
        # Resolve the credential. For -RunAsCurrentUser (no -ServiceCredential) default to
        # the current user and prompt for the password (the SCM needs it to start a
        # named-account service at boot); for -ServiceCredential the credential is already
        # supplied. The prompt is a host-UI call, so it works even though install.ps1 is
        # documented as iwr-piped -- but the service switches are realistically run from a
        # downloaded script.
        if (-not $Credential)
        {
            $defaultUser = "$env:USERDOMAIN\$env:USERNAME"
            $Credential  = Get-Credential -UserName $defaultUser -Message "Password for $defaultUser -- the account the CollabhostUser boot service will run as."
        }
        if (-not $Credential)
        {
            throw 'A credential is required to run the user-scope service under a real account. Supply the password when prompted, pass -ServiceCredential, or drop -RunAsCurrentUser to register the passwordless machine-managed virtual account (the default -- the right choice for a Microsoft-account / PIN sign-in). See INSTALL.md section 5.5.5.'
        }

        # Normalize the operator-supplied name to the 'MACHINE\name' form both New-Service
        # and icacls accept (see Get-QualifiedAccountName -- a bare or '.\' name from
        # Get-Credential / -ServiceCredential fails one or the other). Rebuild the credential
        # on the normalized name so New-Service registers under a name it accepts; the same
        # $accountName then flows to the icacls grant, the SID lookup, the account-change
        # guard, and every operator-facing message.
        $accountName = Get-QualifiedAccountName -AccountName $Credential.UserName
        if ($accountName -ne $Credential.UserName)
        {
            $Credential = New-Object System.Management.Automation.PSCredential($accountName, $Credential.Password)
        }
        $sidBytes    = Resolve-AccountSid -AccountName $accountName

        # A dedicated (non-operator) account does not own the install tree the way the
        # operator's own account does (the installer created $BinDir as the operator), so
        # it needs the same scoped icacls grant the virtual account gets. The operator's
        # own account already owns the tree -- no grant, preserving the pre-existing
        # operator-account behavior.
        $needsFilesystemGrant = -not (Test-IsInstallerIdentity -AccountName $accountName)
    }

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
        # An in-place upgrade does NOT re-assign the service's logon identity beyond
        # the binary-path update, so silently switching the account (operator <->
        # virtual account, or one operator account to another) would leave the service
        # running as the old principal while this run grants/scopes the new one -- a
        # silently-ignored override. Detect a genuine account change and fail loud
        # BEFORE granting anything. (See Test-SameServiceAccount for the SID-vs-string
        # comparison that makes both human and virtual-account forms compare correctly.)
        $registeredAccount = (Get-CimInstance -ClassName Win32_Service -Filter "Name='$UserServiceName'" -ErrorAction SilentlyContinue).StartName
        if ($registeredAccount -and -not (Test-SameServiceAccount -Registered $registeredAccount -Desired $accountName))
        {
            $reRunWith = if ($VirtualAccount) { '-RegisterUserService (the default passwordless virtual account)' } else { '-RegisterUserService with the intended -ServiceCredential or -RunAsCurrentUser' }
            throw "$UserServiceName already runs as '$registeredAccount'. Changing the service account on an in-place upgrade is not supported -- run 'install.ps1 -UnregisterUserService' first, then re-run $reRunWith."
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

    # The 'Log on as a service' right: the operator-account model must be granted it
    # explicitly (and we record whether we added it, to revoke exactly that at
    # uninstall). A virtual account is auto-granted the right by the SCM when it is set
    # as the logon identity -- nothing to grant here, and nothing to revoke later.
    if ($VirtualAccount)
    {
        $weOwnTheGrant = $false
    }
    else
    {
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
    }

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
        Write-Host "$UserServiceName service already registered -- updating binary path (logon identity preserved by the SCM)..."
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
    elseif ($VirtualAccount)
    {
        Write-Host "Registering $UserServiceName service (boot-start, runs as '$accountName')..."
        # New-Service has no virtual-account credential form (-Credential wants a
        # PSCredential, i.e. a password), so create the service, then set its logon
        # identity to the virtual account with sc.exe. The obj= token-less form is
        # required: a virtual account carries no password, and passing `password= '""'`
        # makes the SCM see a 2-char password and reject the account with exit 1057
        # on some Windows editions (the same bug fixed in install-system.ps1). The
        # service exists as LocalSystem only for the instant between these two calls
        # and is never started until its identity is the virtual account (Start-Service
        # is the last step).
        New-Service `
            -Name           $UserServiceName `
            -DisplayName    $UserServiceDisplayName `
            -Description    $UserServiceDescription `
            -BinaryPathName $binaryPathArg `
            -StartupType    Automatic | Out-Null

        & sc.exe config $UserServiceName obj= $UserServiceAccount start= delayed-auto depend= Tcpip | Out-Null
        if ($LASTEXITCODE -ne 0)
        {
            throw "sc.exe config $UserServiceName account/start/depend failed with exit code $LASTEXITCODE."
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

    # Grant the service account scoped access to the install tree when it does not already
    # own it: the virtual account (never the operator) and a dedicated named account both
    # need it; the operator's own account already owns the tree and is skipped. Grant Modify
    # on the whole install tree -- binary, wwwroot, appsettings, data/, and dotnet-bundle/
    # all live together under $BinDir for a user-scope install, and this covers every
    # writable subdir the binary creates at runtime (data/, logs) without having to
    # enumerate them. This is NOT wider than the operator-account model, which runs as the
    # operator who already owns the whole tree. Run after the service exists so a virtual
    # account name resolves; the ACEs live on the directories, so removing the install
    # removes the grant with it (mirrors install-system.ps1).
    if ($needsFilesystemGrant)
    {
        Write-Host "Granting '$accountName' access to the install tree ($BinDir)..."
        & icacls.exe $BinDir /grant "${accountName}:(OI)(CI)M" /T /C | Out-Null
        if ($LASTEXITCODE -ne 0)
        {
            throw "icacls grant on '$BinDir' for '$accountName' failed with exit code $LASTEXITCODE."
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

    # Pre-create the Event Log source so the service's warnings and errors reach the
    # Application log. The service runs headless (no console), so the log is where an
    # operator sees them. The first-boot admin key is not among them: it is saved to
    # data\initial-admin-key.txt.
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
        Write-Host "  The service's warnings and errors may not reach the Application event log. If you cannot"
        Write-Host "  find the first-boot admin key in $InstallPath\data\initial-admin-key.txt (or, on releases"
        Write-Host "  from before that file, in the event log), set Auth:AdminKey in $InstallPath\appsettings.json"
        Write-Host "  to a known value and restart the service (break-glass; see INSTALL.md section 2)."
    }

    Write-Host "Starting $UserServiceName..."
    try
    {
        Start-Service -Name $UserServiceName -ErrorAction Stop
    }
    catch
    {
        Write-Host "Warning: Start-Service $UserServiceName failed -- $($_.Exception.Message)"
        if ($VirtualAccount)
        {
            Write-Host "  Confirm '$accountName' has access to $BinDir (icacls) and the binary path is"
            Write-Host "  correct, then re-run -RegisterUserService -VirtualAccount."
        }
        else
        {
            Write-Host "  A logon failure (error 1069/1326) usually means a wrong password or a missing"
            Write-Host "  'Log on as a service' right. Verify the credential and re-run -RegisterUserService."
        }
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
    Write-Host 'First boot saves the admin key once, to a file:'
    Write-Host "  Get-Content `"$InstallPath\data\initial-admin-key.txt`"   (then delete the file)"
    Write-Host '  No such file? Releases from before the key file logged it to the Application event log instead:'
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
    # LocalSystem/LocalService/NetworkService are built-in principals, and a virtual
    # account (NT SERVICE\*, the -VirtualAccount path) is auto-granted the right by the
    # SCM -- none of them ever needed an explicit grant, so skip them outright as a
    # second guard. (A -VirtualAccount registration also leaves the marker at 0, so
    # $weOwnTheGrant is already false for it; this is belt-and-suspenders.)
    $builtInAccounts = @('LocalSystem', 'NT AUTHORITY\LocalService', 'NT AUTHORITY\NetworkService', 'NT AUTHORITY\SYSTEM')
    if ($weOwnTheGrant -and $accountName -and ($builtInAccounts -notcontains $accountName) -and ($accountName -notlike 'NT SERVICE\*'))
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

# The service-principal flags modify -RegisterUserService. The passwordless virtual account
# is the default; -ServiceCredential (a specific/dedicated account) and -RunAsCurrentUser
# (the signed-in operator) are the real-account opt-ins, and -VirtualAccount is the explicit
# spelling of the default. The real-account opt-ins and the virtual account are mutually
# exclusive -- fail loud on a contradictory invocation rather than silently ignoring a flag.
if ($VirtualAccount -and -not $RegisterUserService)
{
    [Console]::Error.WriteLine('-VirtualAccount only applies together with -RegisterUserService.')
    exit 1
}
if ($RunAsCurrentUser -and -not $RegisterUserService)
{
    [Console]::Error.WriteLine('-RunAsCurrentUser only applies together with -RegisterUserService.')
    exit 1
}
if ($ServiceCredential -and -not $RegisterUserService)
{
    [Console]::Error.WriteLine('-ServiceCredential only applies together with -RegisterUserService.')
    exit 1
}
if ($VirtualAccount -and $ServiceCredential)
{
    [Console]::Error.WriteLine('-VirtualAccount and -ServiceCredential are mutually exclusive: -VirtualAccount registers the passwordless machine-managed account NT SERVICE\CollabhostUser, which takes no credential.')
    exit 1
}
if ($RunAsCurrentUser -and $VirtualAccount)
{
    [Console]::Error.WriteLine('-RunAsCurrentUser and -VirtualAccount are mutually exclusive: -RunAsCurrentUser runs the service as your signed-in account, -VirtualAccount as the passwordless machine-managed account NT SERVICE\CollabhostUser.')
    exit 1
}
if ($RunAsCurrentUser -and $ServiceCredential)
{
    [Console]::Error.WriteLine('-RunAsCurrentUser and -ServiceCredential are mutually exclusive: -RunAsCurrentUser runs the service as your signed-in account, -ServiceCredential as a specific account you name.')
    exit 1
}

# Resolve the service-principal model. The passwordless virtual account is the default
# (no operator password, survives reboot with no interactive login -- the right choice for
# a Microsoft-account / PIN sign-in). A real account is used only when the operator opts in
# with -ServiceCredential or -RunAsCurrentUser. -VirtualAccount is the explicit spelling of
# the default. (Mutually-exclusive combinations were rejected above.)
$useVirtualAccount = $VirtualAccount -or -not ($ServiceCredential -or $RunAsCurrentUser)

if ($WantsServiceAction)
{
    # The service switches register a machine-level Windows service (HKLM),
    # pre-create an event-log source, and (for a real-account model) grant an LSA
    # right -- all need elevation. The plain install (no switch) stays non-elevated
    # and writes only under $HOME. Mirrored from install-system.ps1: hard-fail
    # rather than UAC-relaunch, which does not compose with the iwr-pipe-to-iex flow.
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
    # runbook can compare against /api/v1/version.wwwrootHash. Optional
    # for archives predating the wwwroot-hash sidecar -- absence is silent.
    $WwwrootSidecarSrc = Join-Path $ExtractDir 'wwwroot.sha256'
    if (Test-Path -LiteralPath $WwwrootSidecarSrc)
    {
        Copy-Item -LiteralPath $WwwrootSidecarSrc -Destination $InstallPath -Force
    }

    # appsettings.json: smart-merge on upgrade, plain copy on first install.
    #
    # First install: copy the archive's shipped appsettings.json into place AND seed the sidecar
    # baseline (appsettings.shipped.json) so the next upgrade has a reference for distinguishing
    # operator-edited keys from untouched defaults.
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
        # the gate (the prior regex required 'v' that the binary
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
        Write-Host "  (Current releases also save a copy to $InstallPath\data\initial-admin-key.txt; delete it once the key is stored.)"
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
        Register-CollabhostUserService -BinDir $InstallPath -Credential $ServiceCredential -VirtualAccount:$useVirtualAccount
    }
}
finally
{
    if (Test-Path -LiteralPath $TmpDir)
    {
        Remove-Item -LiteralPath $TmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
}
