param(
    [switch]$AllowNoFfmpeg,
    [string]$CertificatePath = "",
    [string]$CertificatePassword = $env:SNIPEASY_CERT_PASSWORD
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $root "SnipEasy.App\SnipEasy.App.csproj"
[xml]$projectXml = Get-Content -LiteralPath $projectPath
$targetFramework = @($projectXml.Project.PropertyGroup.TargetFramework) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($targetFramework)) {
    throw "TargetFramework was not found in $projectPath."
}

$releaseOutput = Join-Path $root "SnipEasy.App\bin\Release\$targetFramework"
$issPath = Join-Path $PSScriptRoot "SnipEasy.iss"
$setupIcon = Join-Path $root "SnipEasy.App\Assets\SnipEasy.ico"
$ffmpegSource = Join-Path $root "tools\ffmpeg\ffmpeg.exe"
$outputDir = Join-Path $root "website\downloads"

# --- Verify build output ---
if (-not (Test-Path (Join-Path $releaseOutput "SnipEasy.exe"))) {
    throw "Release output was not found. Build SnipEasy.App in Release mode first."
}

if (-not (Test-Path $issPath)) {
    throw "Inno Setup script was not found: $issPath"
}

# --- Locate iscc.exe ---
$isccPath = $null

# 1. Check PATH
$onPath = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
if ($onPath) {
    $isccPath = $onPath.Source
}

# 2. Check default install locations
if (-not $isccPath) {
    $candidates = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\iscc.exe",
        "$env:ProgramFiles\Inno Setup 6\iscc.exe"
    )
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            $isccPath = $candidate
            break
        }
    }
}

if (-not $isccPath) {
    throw @"
Inno Setup 6 was not found. Please install it from https://jrsoftware.org/isinfo.php
After installation, either add it to PATH or install to the default location.
"@
}

Write-Host "Using Inno Setup: $isccPath" -ForegroundColor Cyan

# --- ffmpeg check ---
$ffmpegDir = ""
if (Test-Path $ffmpegSource) {
    $ffmpegDir = Join-Path $root "tools\ffmpeg"
    Write-Host "Bundling ffmpeg from: $ffmpegDir" -ForegroundColor Cyan
}
elseif (-not $AllowNoFfmpeg) {
    throw "ffmpeg.exe was not found at $ffmpegSource. Put ffmpeg.exe there or rerun with -AllowNoFfmpeg for a degraded installer."
}
else {
    Write-Warning "ffmpeg.exe was not found. The installer will be built without bundled MP4/audio recording support."
}

# --- Prepare output directory ---
if (-not (Test-Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}

# --- Remove old installer ---
$setupOutput = Join-Path $outputDir "SnipEasy-Setup.exe"
if (Test-Path $setupOutput) {
    Remove-Item -LiteralPath $setupOutput -Force
}

# --- Read version from exe ---
$exePath = Join-Path $releaseOutput "SnipEasy.exe"
$rawVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exePath).ProductVersion
$version = ($rawVersion -split '[+-]')[0]
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = "1.0.0"
}
Write-Host "App version: $version" -ForegroundColor Cyan

# --- Build defines ---
$defines = @(
    "/DSourceDir=$releaseOutput",
    "/DOutputDir=$outputDir",
    "/DOutputBaseFilename=SnipEasy-Setup",
    "/DAppVersion=$version"
)

if ($setupIcon -and (Test-Path $setupIcon)) {
    $defines += "/DAppIcon=$setupIcon"
}

if ($ffmpegDir) {
    $defines += "/DFfmpegDir=$ffmpegDir"
}

# --- Compile ---
Write-Host "Compiling installer..." -ForegroundColor Cyan
$isccArgs = $defines + @($issPath)

& $isccPath @isccArgs
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
}

# --- Verify output ---
if (-not (Test-Path $setupOutput)) {
    throw "SnipEasy-Setup.exe was not created."
}

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    if (-not (Test-Path -LiteralPath $CertificatePath)) {
        throw "Signing certificate was not found: $CertificatePath"
    }

    $signTool = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if (-not $signTool) {
        throw "signtool.exe was not found. Install the Windows SDK or use a Developer Command Prompt."
    }

    $signArgs = @(
        "sign",
        "/fd", "SHA256",
        "/f", $CertificatePath,
        "/tr", "http://timestamp.digicert.com",
        "/td", "SHA256"
    )
    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $signArgs += "/p"
        $signArgs += $CertificatePassword
    }

    $signArgs += $setupOutput
    & $signTool.Source @signArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Installer signing failed with exit code $LASTEXITCODE."
    }
}

Get-Item -LiteralPath $setupOutput
Write-Host "Installer built successfully: $setupOutput" -ForegroundColor Green
