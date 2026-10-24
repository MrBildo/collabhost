<#
.SYNOPSIS
    Asserts that a Windows Collabhost install's proxy is running and has issued
    a certificate.

.DESCRIPTION
    Used by the CI workflows against a real install on a Windows runner, once
    the server answers /api/v1/status. Every check below needs the proxy to be
    able to write its certificate storage, which is the point: an install that
    leaves that storage unwritable passes every check that only asks whether
    Collabhost itself started.

    1. /api/v1/status settles on proxyState "running", portalReachable true and
       no proxyDetail. The proxy creates its local certificate authority when
       Collabhost loads its configuration into it, so a proxy that cannot write
       its storage reports "degraded" here, with the reason in proxyDetail.
    2. A TLS handshake on 127.0.0.1 (port -HttpsPort) for the Portal's host name is answered
       with a certificate for that name, issued by the proxy's local authority,
       and the Portal answers over HTTPS with its HTML page.
    3. With -StorageDir: the local authority's root certificate and the
       certificate for the Portal's host name are on disk in that directory, so
       the storage is where the install says it is.

    On failure it prints the status body and, when -DataDir holds the first-run
    admin key file, the end of the proxy's own log. The key is never printed.

.PARAMETER Url
    Base address of the running instance.

.PARAMETER HostName
    The Portal's host name (portal subdomain + base domain).

.PARAMETER HttpsPort
    The proxy's HTTPS port (443 unless Proxy:ListenAddress moves it).

.PARAMETER StorageDir
    Where the proxy's certificate storage must be.

.PARAMETER DataDir
    The data directory, used only to read the admin key for the log dump.

.PARAMETER ShowLog
    Print the end of the proxy's log on success too (needs -DataDir).
#>
# Write-Host is intentional: this runs as a CI step and its output is the job log.
[Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSAvoidUsingWriteHost', '')]
[CmdletBinding()]
param
(
    [string]$Url = 'http://localhost:58400',
    [string]$HostName = 'collabhost.collab.internal',
    [int]$HttpsPort = 443,
    [string]$StorageDir,
    [string]$DataDir,
    [int]$TimeoutSeconds = 90,
    [switch]$ShowLog
)

$ErrorActionPreference = 'Stop'

function Write-ProxyDiagnostic
{
    param([string]$StatusBody, [string]$DataDirectory)

    Write-Host '--- /api/v1/status ---'
    Write-Host $StatusBody
    Write-ProxyLog -DataDirectory $DataDirectory
}

function Write-ProxyLog
{
    param([string]$DataDirectory)

    if (-not $DataDirectory)
    {
        return
    }
    $keyFile = Join-Path $DataDirectory 'initial-admin-key.txt'
    if (-not (Test-Path -LiteralPath $keyFile))
    {
        Write-Host "(no admin key file at $keyFile, so no proxy log)"
        return
    }
    $key = @([System.IO.File]::ReadAllLines($keyFile) | Where-Object { $_ -ne '' })[0].Trim()
    try
    {
        $logs = Invoke-RestMethod -Uri "$Url/api/v1/apps/proxy/logs" -Headers @{ 'X-User-Key' = $key }
        Write-Host '--- proxy log (last 40 lines) ---'
        @($logs.entries) | Select-Object -Last 40 | ForEach-Object { Write-Host $_.content }
    }
    catch
    {
        Write-Host "(could not read the proxy log: $($_.Exception.Message))"
    }
}

function Get-ServedCertificate
{
    param([string]$ServerName, [int]$Port)

    $client = New-Object System.Net.Sockets.TcpClient
    try
    {
        $client.Connect('127.0.0.1', $Port)
        # Accept any certificate: the check below is who issued it, not whether
        # this runner trusts the proxy's local authority.
        $accept = [System.Net.Security.RemoteCertificateValidationCallback] { $true }
        $ssl = New-Object System.Net.Security.SslStream($client.GetStream(), $false, $accept)
        try
        {
            $ssl.AuthenticateAsClient($ServerName)
            return New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($ssl.RemoteCertificate)
        }
        finally
        {
            $ssl.Dispose()
        }
    }
    finally
    {
        $client.Dispose()
    }
}

$deadline = (Get-Date).AddSeconds($TimeoutSeconds)

# 1. proxyState leaves "starting" (the first configuration load is asynchronous).
$statusBody = ''
$status = $null
while ((Get-Date) -lt $deadline)
{
    try
    {
        $statusBody = (Invoke-WebRequest -UseBasicParsing -Uri "$Url/api/v1/status" -ErrorAction Stop).Content
        $status = $statusBody | ConvertFrom-Json
        if ($status.proxyState -and $status.proxyState -ne 'starting')
        {
            break
        }
    }
    catch
    {
        $status = $null
    }
    Start-Sleep -Seconds 1
}

if ($null -eq $status -or $status.proxyState -ne 'running' -or $status.portalReachable -ne $true -or $null -ne $status.proxyDetail)
{
    Write-ProxyDiagnostic -StatusBody $statusBody -DataDirectory $DataDir
    $state = if ($null -eq $status) { 'no answer' } else { $status.proxyState }
    throw "Expected proxyState 'running' with portalReachable true and no proxyDetail; got '$state'."
}
Write-Host "  ok: proxyState=running, portalReachable=true, no proxyDetail"

# 2. A certificate for the Portal host name, issued by the local authority.
$cert = $null
$lastError = ''
while ((Get-Date) -lt $deadline)
{
    try
    {
        $cert = Get-ServedCertificate -ServerName $HostName -Port $HttpsPort
        if ($cert.Issuer -match 'Caddy Local Authority')
        {
            break
        }
        $lastError = "served a certificate issued by '$($cert.Issuer)'"
    }
    catch
    {
        $lastError = $_.Exception.Message
    }
    $cert = $null
    Start-Sleep -Seconds 2
}

if ($null -eq $cert)
{
    Write-ProxyDiagnostic -StatusBody $statusBody -DataDirectory $DataDir
    throw "No certificate from the proxy's local authority for $HostName on 127.0.0.1:$HttpsPort within ${TimeoutSeconds}s ($lastError)."
}

$dnsName = $cert.GetNameInfo([System.Security.Cryptography.X509Certificates.X509NameType]::DnsName, $false)
Write-Host "  ok: TLS for $HostName served '$dnsName', issued by '$($cert.Issuer)', valid until $($cert.NotAfter.ToString('u'))"
if ($dnsName -ne $HostName)
{
    throw "The certificate served for $HostName names '$dnsName'."
}

$curl = Join-Path $env:SystemRoot 'System32\curl.exe'
$page = & $curl -k -sS --fail --max-time 30 --resolve "${HostName}:${HttpsPort}:127.0.0.1" "https://${HostName}:$HttpsPort/" 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $page -notmatch '(?i)<!doctype html')
{
    Write-Host $page
    throw "https://${HostName}:$HttpsPort/ did not answer with the Portal's HTML page (curl exit $LASTEXITCODE)."
}
Write-Host "  ok: https://${HostName}:$HttpsPort/ answers with the Portal's HTML page"

# 3. The storage is where the install says it is.
if ($StorageDir)
{
    $expected = @(
        (Join-Path $StorageDir 'pki\authorities\local\root.crt'),
        (Join-Path $StorageDir "certificates\local\$HostName\$HostName.crt")
    )
    foreach ($file in $expected)
    {
        if (-not (Test-Path -LiteralPath $file -PathType Leaf))
        {
            Get-ChildItem -LiteralPath $StorageDir -Recurse -Force -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName | Write-Host
            throw "Expected the proxy's storage in $StorageDir, but $file is not there."
        }
        Write-Host "  ok: $file"
    }
}

if ($ShowLog)
{
    Write-ProxyLog -DataDirectory $DataDir
}

Write-Host 'Proxy check passed.'
