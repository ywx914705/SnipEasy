@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install-SnipEasy.ps1"
if errorlevel 1 (
  echo.
  echo SnipEasy installation did not finish successfully.
  pause
)
