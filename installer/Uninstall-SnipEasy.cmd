@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-SnipEasy.ps1"
if errorlevel 1 (
  echo.
  echo SnipEasy uninstall did not finish successfully.
  pause
)
