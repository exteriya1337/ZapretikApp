@echo off
setlocal EnableExtensions
cd /d "%~dp0"

where powershell >nul 2>&1
if errorlevel 1 (
  echo ERROR: PowerShell not found.
  echo Install Windows PowerShell or copy ZapretikApp.exe to:
  echo   %LOCALAPPDATA%\Zapretik\
  pause
  exit /b 1
)

if not exist "%~dp0ZapretikSetup.ps1" (
  echo ERROR: ZapretikSetup.ps1 not found next to this .bat file.
  echo Keep both files in the same installer folder:
  echo   installer\ZapretikSetup.bat
  echo   installer\ZapretikSetup.ps1
  pause
  exit /b 1
)

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0ZapretikSetup.ps1"
set "ERR=%ERRORLEVEL%"
if not "%ERR%"=="0" (
  echo.
  echo Install failed. Exit code: %ERR%
  pause
  exit /b %ERR%
)
exit /b 0