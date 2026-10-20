<#
.SYNOPSIS
  Publish DayList self-contained win-x64 and build DayList-Setup.exe with Inno Setup.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\publish-installer.ps1
  powershell -ExecutionPolicy Bypass -File scripts\publish-installer.ps1 -Version 1.2.3
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.2.3",
    [switch]$SkipPublish,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $Root "DayList.sln"))) {
    throw "Cannot locate DayList.sln from scripts folder."
}
Set-Location $Root

$dotnet = $null
foreach ($c in @(
    (Get-Command dotnet -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
    "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe",
    "$env:ProgramFiles\dotnet\dotnet.exe"
)) {
    if ($c -and (Test-Path $c)) { $dotnet = $c; break }
}
if (-not $dotnet) { throw "dotnet not found. Install .NET 8 SDK." }

$pf86 = ${env:ProgramFiles(x86)}
$iscc = $null
foreach ($c in @(
    (Join-Path $pf86 "Inno Setup 6\ISCC.exe"),
    (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe"),
    (Get-Command iscc -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source)
)) {
    if ($c -and (Test-Path $c)) { $iscc = $c; break }
}

$publishDir = Join-Path $Root "artifacts\publish\$Runtime"
$distDir = Join-Path $Root "dist"
$csproj = Join-Path $Root "src\DayList.App\DayList.App.csproj"
$iss = Join-Path $Root "installer\DayList.iss"

New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

Write-Host "== DayList publish-installer ==" -ForegroundColor Cyan
Write-Host "Root:       $Root"
Write-Host "PublishDir: $publishDir"
Write-Host "Version:    $Version"
Write-Host "Runtime:    $Runtime"

if (-not $SkipPublish) {
    Write-Host ""
    Write-Host "[1/2] dotnet publish (self-contained + WindowsAppSDKSelfContained)..." -ForegroundColor Cyan
    if (Test-Path $publishDir) {
        Get-ChildItem $publishDir -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    }
    New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

    & $dotnet publish $csproj `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:Platform=x64 `
        -p:WindowsPackageType=None `
        -p:WindowsAppSDKSelfContained=true `
        -p:PublishReadyToRun=true `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -p:Version=$Version `
        -p:AssemblyVersion=$Version.0 `
        -p:FileVersion=$Version.0 `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit $LASTEXITCODE)" }

    $exe = Join-Path $publishDir "DayList.exe"
    if (-not (Test-Path $exe)) { throw "Missing DayList.exe in $publishDir" }

    $sizeMB = [math]::Round(((Get-ChildItem $publishDir -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
    Write-Host "Publish OK: $sizeMB MB -> $publishDir" -ForegroundColor Green
} else {
    Write-Host "[1/2] SkipPublish - using existing $publishDir" -ForegroundColor Yellow
}

if ($SkipInstaller) {
    Write-Host "[2/2] SkipInstaller" -ForegroundColor Yellow
    return
}

if (-not $iscc) {
    Write-Host ""
    Write-Host "[2/2] Inno Setup 6 (ISCC.exe) not found." -ForegroundColor Yellow
    Write-Host "Self-contained publish is ready at: $publishDir"
    Write-Host "Install Inno Setup 6 from https://jrsoftware.org/isinfo.php then re-run."
    exit 2
}

Write-Host ""
Write-Host "[2/2] Compiling installer with Inno Setup..." -ForegroundColor Cyan
& $iscc "/DPublishDir=$publishDir" "/DOutputDir=$distDir" "/DMyAppVersion=$Version" $iss

if ($LASTEXITCODE -ne 0) { throw "ISCC failed (exit $LASTEXITCODE)" }

$setup = Get-ChildItem $distDir -Filter "DayList-Setup-*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $setup) { throw "Setup.exe not found in dist" }

$setupMB = [math]::Round($setup.Length/1MB, 1)
Write-Host ""
Write-Host ("Done: {0} ({1} MB)" -f $setup.FullName, $setupMB) -ForegroundColor Green
Write-Host "User settings stay in %AppData%\DayList after uninstall."
