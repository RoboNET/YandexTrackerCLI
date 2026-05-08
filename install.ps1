#Requires -Version 5.1
<#
.SYNOPSIS
    Installer yt (Yandex Tracker CLI) for Windows.

.DESCRIPTION
    Downloads latest yt-win-x64.zip release from GitHub, verifies SHA256,
    extracts yt.exe to install dir and adds it to user PATH.

.PARAMETER Version
    Release version without 'v' prefix (e.g. 0.1.2). Defaults to latest.

.PARAMETER InstallDir
    Target directory. Default: $env:LOCALAPPDATA\Programs\yt

.PARAMETER NoPath
    Skip user PATH modification.

.EXAMPLE
    irm https://raw.githubusercontent.com/RoboNET/YandexTrackerCLI/main/install.ps1 | iex

.EXAMPLE
    iwr https://raw.githubusercontent.com/RoboNET/YandexTrackerCLI/main/install.ps1 -OutFile install.ps1
    .\install.ps1 -Version 0.1.2
#>
[CmdletBinding()]
param(
    [string]$Version,
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\yt'),
    [switch]$NoPath
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Repo = 'RoboNET/YandexTrackerCLI'
$Asset = 'yt-win-x64.zip'

function Write-Step($msg) { Write-Host "==> $msg" -ForegroundColor Cyan }
function Write-OK($msg)   { Write-Host "    $msg" -ForegroundColor Green }
function Write-Warn2($msg){ Write-Host "    $msg" -ForegroundColor Yellow }

# Arch check — only win-x64 published. ARM64 runs x64 under emulation on Win11.
$arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
if ($arch -ne 'X64' -and $arch -ne 'Arm64') {
    throw "Unsupported architecture: $arch. Only x64 (and arm64 via emulation) supported."
}
if ($arch -eq 'Arm64') {
    Write-Warn2 'ARM64 detected — installing x64 build (runs under Windows emulation).'
}

# Resolve version
if (-not $Version) {
    Write-Step "Fetching latest release tag from github.com/$Repo"
    $latest = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/releases/latest" -Headers @{ 'User-Agent' = 'yt-installer' }
    $Version = $latest.tag_name -replace '^v', ''
    Write-OK "Latest = v$Version"
} else {
    $Version = $Version -replace '^v', ''
}

$baseUrl = "https://github.com/$Repo/releases/download/v$Version"
$zipUrl  = "$baseUrl/$Asset"
$sumsUrl = "$baseUrl/SHA256SUMS"

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("yt-install-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

try {
    Write-Step "Downloading $Asset"
    $zipPath = Join-Path $tmp $Asset
    Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing

    Write-Step 'Verifying SHA256'
    $sumsPath = Join-Path $tmp 'SHA256SUMS'
    Invoke-WebRequest -Uri $sumsUrl -OutFile $sumsPath -UseBasicParsing
    $sumsLine = (Select-String -Path $sumsPath -Pattern ([regex]::Escape($Asset)) | Select-Object -First 1).Line
    if (-not $sumsLine) { throw "SHA256 for $Asset not found in SHA256SUMS." }
    $hashMatch = [regex]::Match($sumsLine, '([A-Fa-f0-9]{64})')
    if (-not $hashMatch.Success) { throw "Could not extract SHA256 from line: $sumsLine" }
    $expected = $hashMatch.Groups[1].Value.ToLower()
    $actual = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToLower()
    if ($actual -ne $expected) {
        throw "SHA256 mismatch.`n  expected: $expected`n  actual:   $actual"
    }
    Write-OK "SHA256 OK ($actual)"

    Write-Step "Installing to $InstallDir"
    if (-not (Test-Path $InstallDir)) {
        New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    }

    # Если yt.exe запущен — Expand-Archive упадёт. Дадим понятную ошибку.
    $targetExe = Join-Path $InstallDir 'yt.exe'
    if (Test-Path $targetExe) {
        try {
            $stream = [System.IO.File]::Open($targetExe, 'Open', 'Read', 'None')
            $stream.Close()
        } catch {
            throw "Cannot overwrite ${targetExe}: file is in use. Close all yt processes and retry."
        }
    }

    Expand-Archive -Path $zipPath -DestinationPath $InstallDir -Force
    Write-OK 'yt.exe installed'

    # PATH
    if (-not $NoPath) {
        $userPath = [Environment]::GetEnvironmentVariable('PATH', 'User')
        $pathParts = if ($userPath) { $userPath.Split(';', [StringSplitOptions]::RemoveEmptyEntries) } else { @() }
        $alreadyOnPath = $pathParts | Where-Object { $_.TrimEnd('\') -ieq $InstallDir.TrimEnd('\') }
        if (-not $alreadyOnPath) {
            Write-Step 'Adding install dir to user PATH'
            $newPath = if ($userPath) { "$userPath;$InstallDir" } else { $InstallDir }
            [Environment]::SetEnvironmentVariable('PATH', $newPath, 'User')
            $env:PATH = "$env:PATH;$InstallDir"
            Write-OK 'PATH updated (open a new terminal for it to take effect globally)'
        } else {
            Write-OK 'Install dir already on user PATH'
        }
    }

    Write-Step 'Verifying install'
    $installed = & $targetExe --version 2>&1
    Write-OK "yt --version → $installed"

    Write-Host ''
    Write-Host "yt v$Version installed to $InstallDir" -ForegroundColor Green
    Write-Host 'Run `yt --help` to get started.' -ForegroundColor Green
}
finally {
    Remove-Item -Path $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
