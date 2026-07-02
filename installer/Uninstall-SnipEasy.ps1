param(
    [switch]$Silent,
    [switch]$KeepUserData,
    [string]$InstallRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Join-Path $env:LOCALAPPDATA "Programs\SnipEasy"
}

$startMenuDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\SnipEasy"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "SnipEasy.lnk"
$dataDirectory = Join-Path $env:LOCALAPPDATA "SnipEasy"

function Write-UninstallMessage([string]$Message, [string]$Color = "Gray") {
    if (-not $Silent) {
        Write-Host $Message -ForegroundColor $Color
    }
}

$processes = @(Get-Process | Where-Object { $_.ProcessName -eq "SnipEasy" })
foreach ($process in $processes) {
    try {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(5000)) {
            Stop-Process -Id $process.Id -Force
        }
    }
    catch {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
    }
}

if (Test-Path $desktopShortcut) {
    Remove-Item -LiteralPath $desktopShortcut -Force
}

if (Test-Path $startMenuDirectory) {
    Remove-Item -LiteralPath $startMenuDirectory -Recurse -Force
}

if (Test-Path $InstallRoot) {
    Remove-Item -LiteralPath $InstallRoot -Recurse -Force
}

if (-not $KeepUserData -and (Test-Path $dataDirectory)) {
    Remove-Item -LiteralPath $dataDirectory -Recurse -Force
}

Write-UninstallMessage "SnipEasy has been removed." Green
if ($KeepUserData) {
    Write-UninstallMessage "User data was kept at: $dataDirectory"
}
