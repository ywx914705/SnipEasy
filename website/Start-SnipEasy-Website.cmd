@echo off
setlocal
cd /d "%~dp0"
set "PYTHON_EXE=C:\Users\Lenovo\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"
if exist "%PYTHON_EXE%" (
  start "SnipEasy Website" "%PYTHON_EXE%" -m http.server 8088 --bind 127.0.0.1
) else (
  start "SnipEasy Website" python -m http.server 8088 --bind 127.0.0.1
)
timeout /t 1 >nul
start http://127.0.0.1:8088/
