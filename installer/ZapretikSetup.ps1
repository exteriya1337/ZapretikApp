#Requires -Version 5.0
<#
.SYNOPSIS
  Installs Zapretik into %LOCALAPPDATA%\Zapretik (not next to this script).
#>
$ErrorActionPreference = "Stop"

$AppName = "Zapretik"
$ExeName = "ZapretikApp.exe"
$InstallDir = Join-Path $env:LOCALAPPDATA "Zapretik"
$ReleaseUrl = "https://github.com/exteriya1337/ZapretikApp/releases/download/v1.0.0/ZapretikApp.exe"
$ConfigUrl  = "https://github.com/exteriya1337/ZapretikApp/releases/download/v1.0.0/ZapretikApp.exe.config"

function Write-Step($msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Ensure-Dir($path) {
    if (-not (Test-Path -LiteralPath $path)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
}

function Get-SourceExe {
    # 1) Same folder as setup (for offline install)
    $here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
    $local = Join-Path $here $ExeName
    if (Test-Path -LiteralPath $local) {
        return @{ Path = $local; From = "local" }
    }

    # 2) Parent folder (repo layout: installer\ + Desktop copy)
    $parent = Split-Path -Parent $here
    $local2 = Join-Path $parent $ExeName
    if (Test-Path -LiteralPath $local2) {
        return @{ Path = $local2; From = "local" }
    }

    $desktop = Join-Path $env:USERPROFILE "Desktop\$ExeName"
    if (Test-Path -LiteralPath $desktop) {
        return @{ Path = $desktop; From = "local" }
    }

    # 3) Download from GitHub Releases
    $tmp = Join-Path $env:TEMP "Zapretik_setup_download"
    Ensure-Dir $tmp
    $dest = Join-Path $tmp $ExeName
    Write-Step "Скачивание $ExeName с GitHub Releases..."
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $ReleaseUrl -OutFile $dest -UseBasicParsing
    } catch {
        throw "Не удалось скачать приложение:`n$($_.Exception.Message)`n`nURL: $ReleaseUrl"
    }
    if (-not (Test-Path $dest) -or (Get-Item $dest).Length -lt 1024) {
        throw "Скачанный файл пуст или повреждён."
    }
    return @{ Path = $dest; From = "download" }
}

function New-Shortcut($shortcutPath, $targetPath, $arguments, $description) {
    $wsh = New-Object -ComObject WScript.Shell
    $sc = $wsh.CreateShortcut($shortcutPath)
    $sc.TargetPath = $targetPath
    if ($arguments) { $sc.Arguments = $arguments }
    $sc.WorkingDirectory = Split-Path -Parent $targetPath
    $sc.Description = $description
    $sc.Save()
}

Write-Host ""
Write-Host "  Zapretik Setup" -ForegroundColor Magenta
Write-Host "  Установка в: $InstallDir" -ForegroundColor DarkGray
Write-Host ""

# Stop running app (so we can overwrite)
Get-Process -Name "ZapretikApp" -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Step "Закрытие запущенного Zapretik..."
    $_ | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
}

$src = Get-SourceExe
Write-Step ("Источник: " + $src.From + " → " + $src.Path)

Ensure-Dir $InstallDir
$destExe = Join-Path $InstallDir $ExeName
Copy-Item -LiteralPath $src.Path -Destination $destExe -Force
Write-Step "Скопировано: $destExe"

# Optional config next to source
$srcConfig = $src.Path + ".config"
$destConfig = $destExe + ".config"
if (Test-Path -LiteralPath $srcConfig) {
    Copy-Item -LiteralPath $srcConfig -Destination $destConfig -Force
} else {
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $ConfigUrl -OutFile $destConfig -UseBasicParsing -ErrorAction SilentlyContinue
    } catch { }
}

# Shortcuts
Write-Step "Ярлыки..."
$desktopLnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "$AppName.lnk"
$startDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
Ensure-Dir $startDir
$startLnk = Join-Path $startDir "$AppName.lnk"

New-Shortcut $desktopLnk $destExe $null "Zapretik"
New-Shortcut $startLnk $destExe $null "Zapretik"

Write-Host ""
Write-Host "  Установка завершена." -ForegroundColor Green
Write-Host "  Папка: $InstallDir" -ForegroundColor Gray
Write-Host "  Ярлык: рабочий стол и меню Пуск" -ForegroundColor Gray
Write-Host ""

$answer = Read-Host "Запустить Zapretik сейчас? (Y/n)"
if ([string]::IsNullOrWhiteSpace($answer) -or $answer -match '^[YyДд]') {
    Start-Process -FilePath $destExe
}

Write-Host ""
Write-Host "Готово. Можно закрыть это окно." -ForegroundColor DarkGray
Start-Sleep -Seconds 2
