<#
.SYNOPSIS
    Asserts the filesystem permissions a Windows Collabhost install leaves behind.

.DESCRIPTION
    Used by the install-integration workflow against real installs on a Windows
    runner. Reads every item under the given path with Get-Acl and checks it
    against the posture the installers promise. Exits non-zero on any
    violation and prints counts, so a pass shows how much it actually checked.

    -Scope System -Expect Restricted
        %ProgramData%\Collabhost: inheritance broken at the root; every item
        carries ACEs for SYSTEM, Administrators and the service account only
        (no BUILTIN\Users, Everyone, Authenticated Users, CREATOR OWNER or
        INTERACTIVE); every item is owned by Administrators or the service
        account; the service account has no access to the root itself, can
        write and delete everything under the writable directories, and can read
        but not write everything under the read-only directories.

    -Scope User -Expect Restricted
        A user-scope install directory: the service account can read every
        item, can write and delete everything under the writable directories,
        and has no write-type right on anything else (the binaries, wwwroot,
        appsettings.json, the directory itself).

    -Expect Open
        The negative control for an upgrade test: asserts the pre-fix posture
        is present (System: BUILTIN\Users reaches the root; User: the service
        account can write collabhost.exe). Exits non-zero if it is not, so an
        upgrade test cannot pass without having started from the broken state.

.PARAMETER AdministratorsSid
    Defaults to BUILTIN\Administrators. Only overridden when exercising this
    script against a scratch tree from a non-elevated session.
#>
# Write-Host is intentional: this runs as a CI step and its output is the job log.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '')]
[CmdletBinding()]
param
(
    [Parameter(Mandatory = $true)][ValidateSet('System', 'User')][string]$Scope,
    [Parameter(Mandatory = $true)][string]$Path,
    [Parameter(Mandatory = $true)][string]$Account,
    [Parameter(Mandatory = $true)][ValidateSet('Restricted', 'Open')][string]$Expect,
    [string[]]$WritableDirs,
    [string[]]$ReadOnlyDirs,
    [string]$AdministratorsSid = 'S-1-5-32-544'
)

$ErrorActionPreference = 'Stop'

if (-not $WritableDirs)
{
    $WritableDirs = if ($Scope -eq 'System') { @('data', 'user-types', 'caddy', 'logs', 'dotnet-bundle') } else { @('data', 'UserTypes', 'dotnet-bundle') }
}
if (-not $ReadOnlyDirs)
{
    $ReadOnlyDirs = if ($Scope -eq 'System') { @('config') } else { @() }
}

$root          = (Resolve-Path -LiteralPath $Path).ProviderPath.TrimEnd('\')
$sidType       = [System.Security.Principal.SecurityIdentifier]
$accountSid    = (New-Object System.Security.Principal.NTAccount($Account)).Translate($sidType).Value
$systemSid     = 'S-1-5-18'
$broadSids     = @{
    'S-1-5-32-545' = 'BUILTIN\Users'
    'S-1-1-0'      = 'Everyone'
    'S-1-5-11'     = 'Authenticated Users'
    'S-1-3-0'      = 'CREATOR OWNER'
    'S-1-5-4'      = 'INTERACTIVE'
}

# FileSystemRights bits.
$readData  = 0x1
$writeData = 0x2
$delete    = 0x10000
# WriteData, AppendData, WriteExtendedAttributes, DeleteSubdirectoriesAndFiles,
# WriteAttributes, Delete, ChangePermissions, TakeOwnership.
$writeMask = 0x2 -bor 0x4 -bor 0x10 -bor 0x40 -bor 0x100 -bor 0x10000 -bor 0x40000 -bor 0x80000

$writableRoots = foreach ($name in $WritableDirs) { Join-Path $root $name }
$readOnlyRoots = foreach ($name in $ReadOnlyDirs) { Join-Path $root $name }

function Test-Under
{
    param([string]$Item, [string[]]$Roots)
    foreach ($r in $Roots)
    {
        if ($Item -eq $r -or $Item.StartsWith($r + '\', [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

# Allow rights the account holds on the item itself (inherit-only ACEs apply to
# children, not to the item, so they are skipped). Deny ACEs are not expected
# anywhere in these trees; any found is reported.
function Get-AccountAccessMask
{
    param($Acl, [string]$Sid)
    $rights = 0
    foreach ($rule in $Acl.GetAccessRules($true, $true, $sidType))
    {
        if ($rule.IdentityReference.Value -ne $Sid) { continue }
        if ($rule.PropagationFlags -band [System.Security.AccessControl.PropagationFlags]::InheritOnly) { continue }
        if ($rule.AccessControlType -ne 'Allow') { continue }
        $rights = $rights -bor [int]$rule.FileSystemRights
    }
    return $rights
}

$items = @(Get-Item -LiteralPath $root -Force) + @(Get-ChildItem -LiteralPath $root -Recurse -Force)
$violations = New-Object System.Collections.Generic.List[string]
$counts = [ordered]@{ Items = $items.Count; Writable = 0; ReadOnly = 0; Other = 0; Owners = @{} }

if ($Expect -eq 'Open')
{
    if ($Scope -eq 'System')
    {
        $rootAcl = Get-Acl -LiteralPath $root
        $usersOnRoot = @($rootAcl.GetAccessRules($true, $true, $sidType) | Where-Object { $_.IdentityReference.Value -eq 'S-1-5-32-545' }).Count
        Write-Host "Open check: BUILTIN\Users ACEs on $root = $usersOnRoot; inheritance protected = $($rootAcl.AreAccessRulesProtected)"
        if ($usersOnRoot -eq 0) { throw "Expected the pre-fix posture (BUILTIN\Users reaching $root) and did not find it." }
    }
    else
    {
        $exe = Join-Path $root 'collabhost.exe'
        $rights = Get-AccountAccessMask (Get-Acl -LiteralPath $exe) $accountSid
        Write-Host ("Open check: {0} rights on {1} = 0x{2:x}" -f $Account, $exe, $rights)
        if (($rights -band $writeData) -eq 0) { throw "Expected the pre-fix posture ($Account can write $exe) and did not find it." }
    }
    Write-Host 'Pre-fix posture present.'
    return
}

$allowedSids = @($systemSid, $AdministratorsSid, $accountSid)

foreach ($item in $items)
{
    $full = $item.FullName.TrimEnd('\')
    $acl  = Get-Acl -LiteralPath $full
    $rules = @($acl.GetAccessRules($true, $true, $sidType))

    foreach ($rule in $rules)
    {
        $sid = $rule.IdentityReference.Value
        if ($rule.AccessControlType -ne 'Allow')
        {
            $violations.Add("${full}: unexpected Deny ACE for $sid")
        }
        if ($Scope -eq 'System')
        {
            if ($broadSids.ContainsKey($sid)) { $violations.Add("${full}: $($broadSids[$sid]) has $($rule.FileSystemRights)") }
            elseif ($allowedSids -notcontains $sid) { $violations.Add("${full}: unexpected principal $sid has $($rule.FileSystemRights)") }
        }
    }

    if ($Scope -eq 'System')
    {
        $owner = $acl.GetOwner($sidType).Value
        $counts.Owners[$owner] = 1 + [int]$counts.Owners[$owner]
        if (@($AdministratorsSid, $accountSid) -notcontains $owner) { $violations.Add("${full}: owned by $owner") }
    }

    $rights = Get-AccountAccessMask $acl $accountSid
    if (Test-Under $full $writableRoots)
    {
        $counts.Writable++
        if ((($rights -band $writeData) -eq 0) -or (($rights -band $delete) -eq 0)) { $violations.Add(("{0}: service account cannot write and delete here (0x{1:x})" -f $full, $rights)) }
    }
    elseif (Test-Under $full $readOnlyRoots)
    {
        $counts.ReadOnly++
        if (($rights -band $writeMask) -ne 0) { $violations.Add(("{0}: service account has a write-type right on a read-only path (0x{1:x})" -f $full, $rights)) }
        if (($rights -band $readData) -eq 0) { $violations.Add(("{0}: service account cannot read (0x{1:x})" -f $full, $rights)) }
    }
    elseif ($Scope -eq 'System' -and $full -eq $root)
    {
        $counts.Other++
        if ($rights -ne 0) { $violations.Add(("{0}: service account holds rights on the root itself (0x{1:x})" -f $full, $rights)) }
    }
    else
    {
        $counts.Other++
        if (($rights -band $writeMask) -ne 0) { $violations.Add(("{0}: service account has a write-type right (0x{1:x})" -f $full, $rights)) }
        if ($Scope -eq 'User' -and (($rights -band $readData) -eq 0)) { $violations.Add(("{0}: service account cannot read (0x{1:x})" -f $full, $rights)) }
    }
}

if ($Scope -eq 'System')
{
    $rootAcl = Get-Acl -LiteralPath $root
    if (-not $rootAcl.AreAccessRulesProtected) { $violations.Add("${root}: inherits its ACL from the parent directory") }
}

$ownerSummary = ($counts.Owners.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" }) -join ', '
Write-Host ("Checked {0} items under {1}: {2} under writable dirs, {3} under read-only dirs, {4} other. Owners: {5}" -f $counts.Items, $root, $counts.Writable, $counts.ReadOnly, $counts.Other, $(if ($ownerSummary) { $ownerSummary } else { 'not checked' }))
if ($counts.Writable -eq 0) { $violations.Add('No items found under the writable directories: the check covered nothing there.') }

if ($violations.Count -gt 0)
{
    Write-Host "$($violations.Count) violation(s):"
    $violations | Select-Object -First 50 | ForEach-Object { Write-Host "  $_" }
    throw "Install ACL check failed with $($violations.Count) violation(s)."
}

Write-Host 'Install ACL check passed.'
