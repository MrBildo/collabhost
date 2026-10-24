# Tests for the Windows installers' download policy: Assert-DownloadUrl and
# Assert-DownloadResponse in docs/install.ps1 and docs/install-system.ps1.
#
# The policy: downloads are HTTPS-only. Plain http:// is accepted only for a
# host on this machine, or for any host when COLLABHOST_INSTALL_ALLOW_HTTP=1 is
# set. Everything else is refused. The same rule is applied again to the
# address a download finally came from, because Windows PowerShell 5.1 follows
# an https -> http redirect.
#
# What this checks:
#   1. Both installers parse, and carry identical copies of both functions.
#   2. A table of URLs, including spellings built to slip a remote host past the
#      loopback check, gets the expected verdict.
#   3. The final-address check, on both response shapes (Windows PowerShell 5.1
#      and PowerShell 7), accepts and refuses what it should, and refuses a
#      response it cannot read.
#   4. With -Network only: real downloads through an https -> https redirect
#      (accepted) and an https -> http redirect (refused by the runtime or by
#      the final-address check). This needs internet access, so it is opt-in.
#
# The functions are loaded from the installers' syntax trees; no other part of
# either installer runs.
#
# Usage: powershell -File tools/test-install-download-policy.ps1 [-Network]
#        pwsh -File tools/test-install-download-policy.ps1 [-Network]
# Exit 0 = all passed. Run it under both, since the two runtimes differ.

param
(
    [switch]$Network
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$docsDir = Join-Path (Split-Path -Parent $PSScriptRoot) 'docs'
$userScript = Join-Path $docsDir 'install.ps1'
$systemScript = Join-Path $docsDir 'install-system.ps1'

$script:passCount = 0
$script:failCount = 0

function Write-Pass
{
    param([string]$Message)

    $script:passCount++
    Write-Output "  ok: $Message"
}

function Write-Fail
{
    param([string]$Message)

    $script:failCount++
    Write-Output "  FAIL: $Message"
}

function Get-FunctionText
{
    param
    (
        [string]$Path,
        [string]$Name
    )

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0)
    {
        throw "$Path has $($parseErrors.Count) parse errors: $($parseErrors[0].Message)"
    }

    $definitions = @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $Name }, $true))
    if ($definitions.Count -ne 1)
    {
        throw "${Path}: found $($definitions.Count) definitions of $Name"
    }

    return $definitions[0].Extent.Text
}

Write-Output "PowerShell $($PSVersionTable.PSVersion)"

# ---- 1. Both installers carry identical copies ------------------------------

Write-Output '== 1. both installers parse and carry identical copies'
foreach ($functionName in 'Assert-DownloadUrl', 'Assert-DownloadResponse')
{
    $userCopy = Get-FunctionText -Path $userScript -Name $functionName
    $systemCopy = Get-FunctionText -Path $systemScript -Name $functionName
    if ($userCopy -ceq $systemCopy)
    {
        Write-Pass "$functionName identical ($($userCopy.Length) characters)"
    }
    else
    {
        Write-Fail "$functionName differs between install.ps1 and install-system.ps1"
    }
}

. ([scriptblock]::Create((Get-FunctionText -Path $userScript -Name 'Assert-DownloadUrl')))
. ([scriptblock]::Create((Get-FunctionText -Path $userScript -Name 'Assert-DownloadResponse')))

# ---- 2. URL verdicts ---------------------------------------------------------

function Get-UrlVerdict
{
    param
    (
        [string]$Url,
        [string]$AllowHttp
    )

    $saved = $env:COLLABHOST_INSTALL_ALLOW_HTTP
    $env:COLLABHOST_INSTALL_ALLOW_HTTP = $AllowHttp
    try
    {
        # Streams 6 (host) and 3 (warning) carry the note and the warning.
        $output = Assert-DownloadUrl -Label 'TEST' -Url $Url 6>&1 3>&1 | Out-String
        if ($Url -like 'https://*')
        {
            return 'https'
        }

        if ($output -match 'which is this machine')
        {
            return 'loopback'
        }

        if ($output -match 'COLLABHOST_INSTALL_ALLOW_HTTP=1\)')
        {
            return 'optin'
        }

        return "accepted-silently:$output"
    }
    catch
    {
        return 'refused'
    }
    finally
    {
        $env:COLLABHOST_INSTALL_ALLOW_HTTP = $saved
    }
}

Write-Output '== 2. URL verdicts'
$urlCases = @(
    , @('https', 'https://github.com/MrBildo/collabhost/releases/download/v1.9.0', '')
    , @('https', 'HTTPS://github.com/x', '')
    , @('loopback', 'http://localhost:9000/releases/v1.9.0', '')
    , @('loopback', 'http://127.0.0.1:8080', '')
    , @('loopback', 'http://127.1.2.3/x', '')
    , @('loopback', 'http://[::1]:9000/x', '')
    , @('loopback', 'http://0x7f000001/x', '')
    , @('refused', 'http://mirror.example.com/collabhost', '')
    , @('optin', 'http://mirror.example.com/collabhost', '1')
    , @('refused', 'http://mirror.example.com/collabhost', 'yes')
    , @('refused', 'http://127.0.0.1.nip.io/x', '')
    , @('refused', 'http://evil.127.0.0.1/x', '')
    , @('refused', 'http://localhost.example.com/x', '')
    , @('refused', 'http://127.0.0.1@example.com/x', '')
    , @('refused', 'http://localhost:80@example.com/x', '')
    , @('refused', 'http://example.com#@127.0.0.1/', '')
    , @('refused', 'http://example.com?@127.0.0.1/', '')
    , @('refused', 'ftp://example.com/x', '1')
    , @('refused', 'file:///C:/x', '1')
    , @('refused', 'not a url', '')
    , @('refused', '', '')
)
foreach ($case in $urlCases)
{
    $expected, $url, $allowHttp = $case
    $verdict = Get-UrlVerdict -Url $url -AllowHttp $allowHttp
    $tag = ''
    if ($allowHttp)
    {
        $tag = " (ALLOW_HTTP=$allowHttp)"
    }

    if ($verdict -eq $expected)
    {
        Write-Pass "${expected}: '$url'$tag"
    }
    else
    {
        Write-Fail "'$url'$tag expected $expected, got $verdict"
    }
}

# ---- 3. Final-address check on both response shapes --------------------------

function Get-WindowsPowerShellResponse
{
    param([string]$FinalUrl)

    return [pscustomobject]@{ BaseResponse = [pscustomobject]@{ ResponseUri = [uri]$FinalUrl } }
}

function Get-PowerShell7Response
{
    param([string]$FinalUrl)

    return [pscustomobject]@{ BaseResponse = [pscustomobject]@{ RequestMessage = [pscustomobject]@{ RequestUri = [uri]$FinalUrl } } }
}

function Get-ResponseVerdict
{
    param
    (
        $Response,
        [string]$RequestUrl,
        [string]$AllowHttp
    )

    $saved = $env:COLLABHOST_INSTALL_ALLOW_HTTP
    $env:COLLABHOST_INSTALL_ALLOW_HTTP = $AllowHttp
    try
    {
        Assert-DownloadResponse -Response $Response -RequestUrl $RequestUrl -Label 'archive'
        return 'accepted'
    }
    catch
    {
        return 'refused'
    }
    finally
    {
        $env:COLLABHOST_INSTALL_ALLOW_HTTP = $saved
    }
}

Write-Output '== 3. final-address rules on both response shapes'
$responseCases = @(
    , @('accepted', 'https://github.com/a', 'https://objects.githubusercontent.com/b', '')
    , @('refused', 'https://github.com/a', 'http://127.0.0.1/b', '')
    , @('refused', 'https://github.com/a', 'http://example.com/b', '')
    , @('accepted', 'https://github.com/a', 'http://example.com/b', '1')
    , @('accepted', 'http://127.0.0.1:9000/a', 'http://127.0.0.1:9000/a', '')
    , @('refused', 'http://127.0.0.1:9000/a', 'http://example.com/b', '')
    , @('refused', 'https://github.com/a', 'ftp://example.com/b', '1')
)
foreach ($case in $responseCases)
{
    $expected, $requestUrl, $finalUrl, $allowHttp = $case
    $tag = ''
    if ($allowHttp)
    {
        $tag = ' (ALLOW_HTTP=1)'
    }

    foreach ($shape in 'Get-WindowsPowerShellResponse', 'Get-PowerShell7Response')
    {
        $verdict = Get-ResponseVerdict -Response (& $shape -FinalUrl $finalUrl) -RequestUrl $requestUrl -AllowHttp $allowHttp
        if ($verdict -eq $expected)
        {
            Write-Pass "${shape} ${expected}: $requestUrl -> $finalUrl$tag"
        }
        else
        {
            Write-Fail "${shape}: $requestUrl -> $finalUrl$tag expected $expected, got $verdict"
        }
    }
}

$unreadable = [pscustomobject]@{ BaseResponse = [pscustomobject]@{ Other = 1 } }
if ((Get-ResponseVerdict -Response $unreadable -RequestUrl 'https://github.com/a' -AllowHttp '') -eq 'refused')
{
    Write-Pass 'a response whose final address cannot be read is refused'
}
else
{
    Write-Fail 'a response whose final address cannot be read was accepted'
}

# ---- 4. Real downloads through redirects (opt-in) -----------------------------

if ($Network)
{
    Write-Output '== 4. real downloads through redirects'
    $downloadDir = Join-Path ([IO.Path]::GetTempPath()) ('download-policy-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory $downloadDir | Out-Null
    try
    {
        $httpsToHttps = 'https://httpbin.org/redirect-to?url=https%3A%2F%2Fexample.com%2F'
        $response = Invoke-WebRequest -UseBasicParsing -Uri $httpsToHttps -OutFile (Join-Path $downloadDir 'a') -PassThru
        try
        {
            Assert-DownloadResponse -Response $response -RequestUrl $httpsToHttps -Label 'archive'
            Write-Pass 'https -> https redirect accepted'
        }
        catch
        {
            Write-Fail "https -> https redirect refused: $_"
        }

        # Windows PowerShell 5.1 follows this redirect; PowerShell 7 refuses it.
        $httpsToHttp = 'https://httpbin.org/redirect-to?url=http%3A%2F%2Fexample.com%2F'
        $followed = $null
        try
        {
            $followed = Invoke-WebRequest -UseBasicParsing -Uri $httpsToHttp -OutFile (Join-Path $downloadDir 'b') -PassThru
        }
        catch
        {
            Write-Pass "https -> http redirect refused by the runtime ($($_.Exception.GetType().Name))"
        }

        if ($null -ne $followed)
        {
            try
            {
                Assert-DownloadResponse -Response $followed -RequestUrl $httpsToHttp -Label 'archive'
                Write-Fail 'the runtime followed https -> http and the final-address check accepted it'
            }
            catch
            {
                if ("$_" -match 'requested over HTTPS')
                {
                    Write-Pass 'the runtime followed https -> http; the final-address check refused it'
                }
                else
                {
                    Write-Fail "unexpected error: $_"
                }
            }
        }
    }
    finally
    {
        Remove-Item -Recurse -Force $downloadDir
    }
}
else
{
    Write-Output '== 4. real downloads through redirects: skipped (pass -Network to run)'
}

Write-Output ''
Write-Output "passed: $script:passCount  failed: $script:failCount"
if ($script:failCount -ne 0)
{
    exit 1
}

exit 0
