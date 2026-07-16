param(
    [string]$Configuration = "Release",
    [string]$Runtime = "",
    [switch]$SelfContained,
    [switch]$NoRestore = $true,
    [string]$CertificatePath = "",
    [string]$CertificatePassword = ""
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "SnipEasy.App\SnipEasy.App.csproj"
[xml]$projectXml = Get-Content -LiteralPath $project
$targetFramework = @($projectXml.Project.PropertyGroup.TargetFramework) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($targetFramework)) {
    throw "TargetFramework was not found in $project."
}

$outputRoot = Join-Path $root "dist\publish"
$publishName = if ([string]::IsNullOrWhiteSpace($Runtime)) { "framework-dependent" } else { $Runtime }
$output = Join-Path $outputRoot $publishName

if (Test-Path $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}

$publishArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-o", $output
)

if (-not [string]::IsNullOrWhiteSpace($Runtime)) {
    $publishArgs += "-r"
    $publishArgs += $Runtime
    $publishArgs += "/p:PublishSingleFile=true"
    $publishArgs += "/p:IncludeNativeLibrariesForSelfExtract=true"
}

if ($SelfContained -and [string]::IsNullOrWhiteSpace($Runtime)) {
    throw "Self-contained publish requires -Runtime, for example -Runtime win-x64."
}

if ($SelfContained) {
    $publishArgs += "--self-contained"
    $publishArgs += "true"
}
else {
    $publishArgs += "--self-contained"
    $publishArgs += "false"
}

if ($NoRestore) {
    $publishArgs += "--no-restore"
}

$previousErrorActionPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
& dotnet @publishArgs
$publishExitCode = $LASTEXITCODE
$ErrorActionPreference = $previousErrorActionPreference
if ($publishExitCode -ne 0) {
    Write-Warning "dotnet publish failed with exit code $publishExitCode. Falling back to existing Release build output."
}

$exe = Join-Path $output "SnipEasy.exe"
if (-not (Test-Path $exe)) {
    $releaseOutput = Join-Path $root "SnipEasy.App\bin\$Configuration\$targetFramework"
    $releaseExe = Join-Path $releaseOutput "SnipEasy.exe"
    if (-not (Test-Path $releaseExe)) {
        throw "Publish failed and Release output was not found. Run dotnet build SnipEasy.sln -c $Configuration first."
    }

    New-Item -ItemType Directory -Path $output -Force | Out-Null
    Copy-Item -Path (Join-Path $releaseOutput "*") -Destination $output -Recurse -Force
}

$exe = Join-Path $output "SnipEasy.exe"
if (-not (Test-Path $exe)) {
    throw "Publish failed: SnipEasy.exe was not produced."
}

if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    if (-not (Get-Command signtool.exe -ErrorAction SilentlyContinue)) {
        throw "signtool.exe was not found. Install Windows SDK or run from a Developer Command Prompt."
    }

    $signArgs = @("sign", "/fd", "SHA256", "/f", $CertificatePath, "/tr", "http://timestamp.digicert.com", "/td", "SHA256")
    if (-not [string]::IsNullOrWhiteSpace($CertificatePassword)) {
        $signArgs += "/p"
        $signArgs += $CertificatePassword
    }

    $signArgs += $exe
    & signtool.exe @signArgs
}

$rawVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
$version = ($rawVersion -split '[+-]')[0]
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Unable to determine a valid product version from $rawVersion."
}
$manifest = [ordered]@{
    version = $version
    runtime = $publishName
    selfContained = [bool]$SelfContained
    downloadPath = "SnipEasy-Setup.exe"
    notes = "SnipEasy $version"
}
$manifestPath = Join-Path $outputRoot "update-manifest.json"
$manifest | ConvertTo-Json | Set-Content -LiteralPath $manifestPath -Encoding UTF8

Get-Item -LiteralPath $exe
Get-Item -LiteralPath $manifestPath
