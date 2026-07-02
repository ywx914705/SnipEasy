param(
    [switch]$Silent,
    [switch]$NoLaunch,
    [string]$InstallRoot
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    $InstallRoot = Join-Path $env:LOCALAPPDATA "Programs\SnipEasy"
}

$startMenuDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\SnipEasy"
$desktopShortcut = Join-Path ([Environment]::GetFolderPath("DesktopDirectory")) "SnipEasy.lnk"
$startMenuShortcut = Join-Path $startMenuDirectory "SnipEasy.lnk"
$payloadZip = Join-Path $PSScriptRoot "SnipEasy-Payload.zip"
$payloadExtractRoot = $null
$payloadRoot = $PSScriptRoot
$appSource = Join-Path $payloadRoot "app"

function Write-InstallMessage([string]$Message, [string]$Color = "Gray") {
    if (-not $Silent) {
        Write-Host $Message -ForegroundColor $Color
    }
}

function Stop-SnipEasyProcess {
    $processes = @(Get-Process | Where-Object { $_.ProcessName -eq "SnipEasy" })
    foreach ($process in $processes) {
        Write-InstallMessage "Stopping running SnipEasy process $($process.Id)..." Yellow
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
}

if (Test-Path $payloadZip) {
    $payloadExtractRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("SnipEasySetup_" + [System.Guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Path $payloadExtractRoot -Force | Out-Null
    Expand-Archive -LiteralPath $payloadZip -DestinationPath $payloadExtractRoot -Force
    $payloadRoot = $payloadExtractRoot
    $appSource = Join-Path $payloadRoot "app"
}

$exeSource = Join-Path $appSource "SnipEasy.exe"

try {
    if (-not (Test-Path $exeSource)) {
        throw "The installer package is incomplete. SnipEasy.exe was not found."
    }

    $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exeSource).ProductVersion
    if ([string]::IsNullOrWhiteSpace($version)) {
        $version = "unknown"
    }

    $hasDesktopRuntime = $false
    try {
        $runtimes = & dotnet --list-runtimes 2>$null
        $hasDesktopRuntime = ($runtimes | Where-Object { $_ -match "^Microsoft\.WindowsDesktop\.App 9\." }).Count -gt 0
    }
    catch {
        $hasDesktopRuntime = $false
    }

    if (-not $hasDesktopRuntime) {
        Write-InstallMessage "SnipEasy needs Microsoft .NET 9 Desktop Runtime." Yellow
        if (-not $Silent) {
            Write-InstallMessage "The download page will open now. Install the runtime, then run this installer again."
            Start-Process "https://dotnet.microsoft.com/download/dotnet/9.0"
        }
        exit 2
    }

    Stop-SnipEasyProcess

    New-Item -ItemType Directory -Path $InstallRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null

    Copy-Item -Path (Join-Path $appSource "*") -Destination $InstallRoot -Recurse -Force
    Copy-Item -Path (Join-Path $payloadRoot "Uninstall-SnipEasy.cmd") -Destination $InstallRoot -Force
    Copy-Item -Path (Join-Path $payloadRoot "Uninstall-SnipEasy.ps1") -Destination $InstallRoot -Force

    $exePath = Join-Path $InstallRoot "SnipEasy.exe"
    $shell = New-Object -ComObject WScript.Shell
    foreach ($shortcutPath in @($desktopShortcut, $startMenuShortcut)) {
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $exePath
        $shortcut.WorkingDirectory = $InstallRoot
        $shortcut.IconLocation = $exePath
        $shortcut.Save()
    }

    Write-InstallMessage "SnipEasy $version has been installed." Green
    Write-InstallMessage "Install path: $InstallRoot"

    if (-not $NoLaunch -and -not $Silent) {
        Start-Process $exePath
    }
}
finally {
    if ($payloadExtractRoot -and (Test-Path $payloadExtractRoot)) {
        Remove-Item -LiteralPath $payloadExtractRoot -Recurse -Force
    }
}
