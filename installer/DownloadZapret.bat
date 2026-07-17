@echo off
setlocal EnableExtensions
cd /d "%~dp0"
chcp 65001 >nul 2>&1

where powershell >nul 2>&1
if errorlevel 1 (
  echo ERROR: PowerShell not found.
  pause
  exit /b 1
)

if not exist "%~dp0DownloadZapret.ps1" (
  echo ERROR: DownloadZapret.ps1 not found next to this .bat file.
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0DownloadZapret.ps1" %*
set "ERR=%ERRORLEVEL%"
if not "%ERR%"=="0" (
  echo.
  echo Download failed. Exit code: %ERR%
  pause
  exit /b %ERR%
)
exit /b 0
