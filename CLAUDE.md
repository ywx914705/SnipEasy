# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SnipEasy is a Windows desktop capture workstation built with .NET 9, WPF, and WinForms. It provides hotkey-driven region screenshots (F1) and screen recording (F2) with annotation tools, system tray integration, and clipboard-first workflow. The UI is primarily in Chinese (Simplified).

## Build & Run Commands

```bash
# Build
# Build
dotnet build SnipEasy.sln
dotnet build SnipEasy.sln -c Release

# Run
dotnet run --project SnipEasy.App

# Run tests
dotnet test SnipEasy.App.Tests

# Run automated smoke tests (headless, exits after completion)
dotnet run --project SnipEasy.App -- --capture-once   # Full-screen capture test
dotnet run --project SnipEasy.App -- --record-test     # AVI recording test (2 seconds)

# Build the IExpress installer
powershell -NoProfile -ExecutionPolicy Bypass -File installer/Build-SnipEasy-Setup.ps1
```

The test project (`SnipEasy.App.Tests/`) contains 100+ unit tests covering models, services, and utilities. Verification is also done via the `--capture-once` and `--record-test` CLI flags.

## Architecture

### Single-project layout

Everything lives in `SnipEasy.App/`. The root namespace is `SnipEasy.App`, assembly name is `SnipEasy`.

### Key layers

- **App.xaml.cs** — Entry point. Enforces single-instance via named Mutex. Routes `--capture-once` and `--record-test` CLI args to headless smoke-test paths.
- **MainWindow** — Primary window. Delegates to ViewModels for recording lifecycle and history management. Handles hotkey registration, system tray, and UI event wiring.
- **RegionCaptureWindow** — Full-screen overlay for region selection and annotation (pen, rectangle, ellipse, text, arrow, mosaic, highlight, blur). Renders the final annotated `BitmapSource` for clipboard or file save.
- **RecordingStatusWindow** — Floating status pill during recording (light theme). Shows elapsed timer, stop/save/cancel decision UI. Uses `WdaExcludeFromCapture` to exclude itself from screen capture.

### Services

- **RecordingServiceCoordinator** — Strategy coordinator implementing `IRecordingService`. Prefers `FfmpegRecordingService` (MP4 + audio routing), falls back to `LocalAviRecordingService` (AVI, no audio) if `AllowLocalAviFallback` is enabled. The AVI fallback is off by default to prevent high-CPU recordings.
- **FfmpegRecordingService** — Spawns `ffmpeg.exe` as a child process with DirectShow screen/audio capture. Resolves ffmpeg from settings path, `tools/ffmpeg/ffmpeg.exe`, or PATH.
- **LocalAviRecordingService / AviWriter** — Built-in AVI writer using `System.Drawing` screen bitmaps. No audio support.
- **ScreenCaptureService** — Handles full-screen and virtual-desktop capture using `Graphics.CopyFromScreen`. Resolves screenshot/video directory paths.
- **HotkeyManager** — Registers global hotkeys (F1-F4) via Win32 `RegisterHotKey` with low-level keyboard hook fallback. Supports configurable hotkey strings (e.g., "Ctrl+Shift+S").
- **TrayService** — System tray icon with context menu. Keeps the app running when the main window closes (if `MinimizeToTrayOnClose` is true).
- **ClipboardService** — Wraps `System.Windows.Clipboard` for image and file-drop operations.

### Models

- **AppSettings** — All user-configurable settings, serialized to `%LOCALAPPDATA%/SnipEasy/settings.json`.
- **CaptureRecord** — History entry for a screenshot or recording. Stored in `%LOCALAPPDATA%/SnipEasy/history.json`.
- **RecordingPerformanceProfiles** — Three preset profiles (Smooth/Balanced/Quality) that set frame rate, CRF, and FFmpeg preset.

### Storage

- **JsonFileStore\<T\>** — Generic JSON file persistence with atomic write (write to `.tmp`, copy, delete). Used for settings and history.
- **AppPaths** — Resolves all well-known paths. Data lives in `D:\SnipEasy\Data\`. Default screenshots go to `D:\SnipEasy\Screenshots\`, videos to `D:\SnipEasy\Videos\`.

### Native interop

- **NativeMethods** — P/Invoke declarations for `RegisterHotKey`, `GetForegroundWindow`, `SetWindowDisplayAffinity`, etc. in `SnipEasy.App.Native`.

## Key Conventions

- Recording drafts are written to `%LOCALAPPDATA%/SnipEasy/RecordingDrafts/` during capture. The user explicitly saves or discards after recording stops.
- Settings have legacy migration logic in `AppSettingsService.MigrateLegacySettings()` for watermark templates and directory paths.
- The `--capture-once` and `--record-test` paths are self-contained: they create their own service instances, capture, write history, and `Shutdown()` with exit code 0 on success, 1 on failure.
- History is capped at 1000 entries and filtered by `HistoryRetentionDays` (default 90).
- FFmpeg resolution order: settings `FfmpegPath` → `tools/ffmpeg/ffmpeg.exe` beside the executable → PATH environment variable.

## Installer

The `installer/` directory contains a PowerShell-based IExpress setup builder (`Build-SnipEasy-Setup.ps1`). It packages the Release build output, bundles `ffmpeg.exe` if present in `tools/ffmpeg/`, and produces `website/downloads/SnipEasy-Setup.exe`. Post-install scripts handle Start Menu shortcuts and uninstall registration.
