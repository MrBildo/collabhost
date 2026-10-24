<#
.SYNOPSIS
    Moves a Windows Collabhost install's proxy off ports 80 and 443 for CI.

.DESCRIPTION
    GitHub's Windows runners refuse a bind on port 80 ("An attempt was made to
    access a socket in a way forbidden by its access permissions"), so on the
    default listen address the proxy never loads a configuration there and a
    proxy check can only fail. This sets Proxy:ListenAddress in the install's
    appsettings.json (the setting an operator would use) and, when a service is
    named, restarts it so the running server picks it up. The installers keep an
    existing appsettings.json on a re-run, so the setting survives upgrades.

.PARAMETER SettingsFile
    The install's appsettings.json.

.PARAMETER ServiceName
    The Windows service to restart afterwards, if any.

.PARAMETER ListenAddress
    The proxy listen address to set. With neither port 80 nor 443 in the list,
    Collabhost also turns off the proxy's HTTP-to-HTTPS redirect, which would
    otherwise still bind port 80.
#>
# Write-Host is intentional: this runs as a CI step and its output is the job log.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '')]
[CmdletBinding()]
param
(
    [Parameter(Mandatory = $true)][string]$SettingsFile,
    [string]$ServiceName,
    [string]$ListenAddress = ':8080,:8443'
)

$ErrorActionPreference = 'Stop'

$settings = Get-Content -LiteralPath $SettingsFile -Raw | ConvertFrom-Json
if (-not $settings.PSObject.Properties['Proxy'])
{
    $settings | Add-Member -NotePropertyName 'Proxy' -NotePropertyValue ([pscustomobject]@{})
}
$before = $settings.Proxy.ListenAddress
$settings.Proxy | Add-Member -NotePropertyName 'ListenAddress' -NotePropertyValue $ListenAddress -Force
# Set-Content rewrites the existing file in place, so it keeps the ACL the
# installer put on it.
$settings | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $SettingsFile
$after = (Get-Content -LiteralPath $SettingsFile -Raw | ConvertFrom-Json).Proxy.ListenAddress
Write-Host "Proxy:ListenAddress in ${SettingsFile}: '$before' -> '$after'"
if ($after -ne $ListenAddress)
{
    throw "Proxy:ListenAddress reads back as '$after', not '$ListenAddress'."
}

if ($ServiceName)
{
    Restart-Service -Name $ServiceName
    Write-Host "Restarted $ServiceName ($((Get-Service -Name $ServiceName).Status))."
}
