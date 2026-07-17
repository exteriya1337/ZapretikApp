@echo off
chcp 65001 >nul
title Zapretik Setup
cd /d "%~dp0"

:: Prefer PowerShell 5+
where powershell >nul 2>&1
if errorlevel 1 (
  echo PowerShell не найден. Установите его или скопируйте ZapretikApp.exe вручную в:
  echo   %LOCALAPPDATA%\Zapretik\
  pause
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0ZapretikSetup.ps1"
if errorlevel 1 (
  echo.
  echo Установка завершилась с ошибкой.
  pause
  exit /b 1
)
exit /b 0
