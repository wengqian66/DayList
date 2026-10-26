<#
.SYNOPSIS
  Create/update a GitHub Release and upload the DayList installer.

.DESCRIPTION
  Uses the token already stored by the Git credential manager.

  IMPORTANT: the REST call sends the JSON body as UTF-8 bytes with
  `Content-Type: application/json; charset=utf-8`. Passing a plain
  PowerShell string to -Body (the previous one-off command) made the
  server receive non-UTF-8 bytes and the Chinese release notes showed
  up as "?????". Do not go back to that.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\release.ps1 -Version 1.2.13 -NotesFile docs\release-1.2.13.md
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$NotesFile = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$repo = 'wengqian66/DayList'
$tag = "v$Version"

$notes = "v$Version"
if ($NotesFile -and (Test-Path $NotesFile)) {
    # ReadAllText keeps this a plain string. Get-Content -Raw can hand back a decorated
    # object here, and ConvertTo-Json then serialises the whole PSObject as the body.
    $notes = [System.IO.File]::ReadAllText($NotesFile, [System.Text.Encoding]::UTF8)
}

$credIn = "protocol=https`nhost=github.com`n`n"
$credOut = $credIn | git -C $root credential fill
$token = (($credOut | Select-String '^password=' | ForEach-Object { $_.Line }) -replace '^password=','').Trim()
if (-not $token) {
    throw "No GitHub token found in the credential manager (git credential fill)."
}

$headers = @{
    Authorization = "Bearer $token"
    'User-Agent' = 'DayList'
    Accept = 'application/vnd.github+json'
}
$jsonType = 'application/json; charset=utf-8'

$release = $null
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$repo/releases/tags/$tag" -Headers $headers -TimeoutSec 30
} catch {
    $status = 0
    try { $status = [int]$_.Exception.Response.StatusCode } catch {}
    if ($status -ne 404) { throw }
}

if ($release) {
    $patch = @{ body = $notes } | ConvertTo-Json -Depth 3
    $release = Invoke-RestMethod -Method Patch `
        -Uri "https://api.github.com/repos/$repo/releases/$($release.id)" `
        -Headers $headers `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($patch)) `
        -ContentType $jsonType `
        -TimeoutSec 30
    Write-Host "Updated release $tag."
} else {
    $payload = @{
        tag_name = $tag
        target_commitish = 'main'
        name = $tag
        body = $notes
        draft = $false
        prerelease = $false
    } | ConvertTo-Json -Depth 4
    $release = Invoke-RestMethod -Method Post `
        -Uri "https://api.github.com/repos/$repo/releases" `
        -Headers $headers `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($payload)) `
        -ContentType $jsonType `
        -TimeoutSec 60
    Write-Host "Created release $tag."
}

$setup = Join-Path $root "dist/DayList-Setup-$Version.exe"
if (Test-Path $setup) {
    $upload = $release.upload_url -replace '\{.*\}', ''
    $asset = curl.exe -s -S -X POST `
        -H "Authorization: Bearer $token" `
        -H 'User-Agent: DayList' `
        -H 'Content-Type: application/octet-stream' `
        --data-binary "@$setup" `
        "$upload?name=DayList-Setup-$Version.exe"
    Write-Host $asset
} else {
    Write-Warning "Installer not found: $setup (run publish-installer.ps1 first)."
}

Write-Host "Release page: $($release.html_url)"
