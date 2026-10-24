#Requires -Version 7
# Checks how a first run hands over the admin key Collabhost generates on
# Windows: in initial-admin-key.txt in the data directory (which takes that
# directory's permissions, as the database beside it does), and, when the
# process had a console, once on standard output. The Windows twin of
# tools/assert-admin-key-file.sh; the key itself is never printed.
#
#   -DataDir    the data directory (the one holding collabhost.db).
#   -StdoutLog  the process's captured standard output: it must carry the
#               "Collabhost admin key:" line exactly once, with the key the
#               file holds. Omit it for a service, which has no console.
#   -Url        a running instance: the key must authenticate there as an
#               administrator.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $DataDir,
    [string] $StdoutLog,
    [string] $Url
)

$ErrorActionPreference = 'Stop'

$marker = 'Collabhost admin key:'
$keyFile = Join-Path $DataDir 'initial-admin-key.txt'
$failures = [System.Collections.Generic.List[string]]::new()

$item = Get-Item -LiteralPath $keyFile -Force -ErrorAction SilentlyContinue
if (-not $item -or $item.PSIsContainer -or $item.LinkType) {
    Get-ChildItem -LiteralPath $DataDir -Force -ErrorAction SilentlyContinue | Format-Table Mode, Length, Name | Out-String | Write-Host
    throw "$keyFile is missing or is not a regular file."
}
Write-Host "  ok: $keyFile exists"

$lines = @([System.IO.File]::ReadAllLines($keyFile) | Where-Object { $_ -ne '' })
$key = if ($lines.Count -gt 0) { $lines[0].Trim() } else { '' }
# A ULID: 26 characters of Crockford base32.
if ($lines.Count -eq 1 -and $key -cmatch '^[0-9A-HJKMNP-TV-Z]{26}$') {
    Write-Host '  ok: the file holds one key, shaped as a ULID'
} else {
    $failures.Add("the file does not hold exactly one ULID-shaped line ($($lines.Count) line(s))")
}

if ($StdoutLog) {
    $hits = @(Select-String -LiteralPath $StdoutLog -SimpleMatch -CaseSensitive -Pattern $marker)
    $printed = if ($hits.Count -gt 0) { ($hits[0].Line.Substring($hits[0].Line.IndexOf($marker) + $marker.Length)).Trim() } else { '' }
    if ($hits.Count -eq 1 -and $printed -ceq $key) {
        Write-Host '  ok: standard output printed the key once, and it matches the file'
    } else {
        $failures.Add("standard output carried the key line $($hits.Count) time(s), or a different key than the file")
    }
}

if ($Url) {
    $role = $null
    try {
        $role = (Invoke-RestMethod -Uri "$Url/api/v1/auth/me" -Headers @{ 'X-User-Key' = $key }).role
    } catch {
        $role = "request failed: $($_.Exception.Message)"
    }
    if ($role -ceq 'administrator') {
        Write-Host '  ok: the key authenticates as an administrator'
    } else {
        $failures.Add("the key did not authenticate as an administrator ($role)")
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Host "FAIL: $failure" }
    throw 'Admin key hand-over check failed.'
}
Write-Host 'Admin key hand-over verified.'
