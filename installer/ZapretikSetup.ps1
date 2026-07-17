# Zapretik installer - installs into %LOCALAPPDATA%\Zapretik
# Encoding: save as UTF-8 with BOM for Windows PowerShell 5.x Cyrillic safety
$ErrorActionPreference = "Stop"

$AppName = "Zapretik"
$ExeName = "ZapretikApp.exe"
$InstallDir = Join-Path $env:LOCALAPPDATA "Zapretik"
$ReleaseUrl = "https://github.com/exteriya1337/ZapretikApp/releases/download/v1.0.5/ZapretikApp.exe"
$ConfigUrl  = "https://github.com/exteriya1337/ZapretikApp/releases/download/v1.0.5/ZapretikApp.exe.config"

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Ensure-Dir([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
}

function Get-ScriptDirectory {
    if ($PSScriptRoot) { return $PSScriptRoot }
    return Split-Path -Parent $MyInvocation.MyCommand.Path
}

function Get-SourceExe {
    $here = Get-ScriptDirectory

    $candidates = @(
        (Join-Path $here $ExeName),
        (Join-Path (Split-Path -Parent $here) $ExeName),
        (Join-Path $env:USERPROFILE "Desktop\$ExeName"),
        (Join-Path ([Environment]::GetFolderPath("Desktop")) $ExeName)
    )

    foreach ($local in $candidates) {
        if ($local -and (Test-Path -LiteralPath $local)) {
            return @{ Path = $local; From = "local" }
        }
    }

    $tmp = Join-Path $env:TEMP "Zapretik_setup_download"
    Ensure-Dir $tmp
    $dest = Join-Path $tmp $ExeName
    Write-Step "Downloading $ExeName from GitHub Releases..."
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri $ReleaseUrl -OutFile $dest -UseBasicParsing
    } catch {
        throw "Download failed: $($_.Exception.Message)`nURL: $ReleaseUrl"
    }
    if (-not (Test-Path -LiteralPath $dest) -or ((Get-Item -LiteralPath $dest).Length -lt 1024)) {
        throw "Downloaded file is empty or corrupt."
    }
    return @{ Path = $dest; From = "download" }
}

function New-Shortcut([string]$shortcutPath, [string]$targetPath, [string]$description) {
    $wsh = New-Object -ComObject WScript.Shell
    $sc = $wsh.CreateShortcut($shortcutPath)
    $sc.TargetPath = $targetPath
    $sc.WorkingDirectory = Split-Path -Parent $targetPath
    $sc.Description = $description
    $sc.Save()
}

try {
    Write-Host ""
    Write-Host "  Zapretik Setup 1.0.5" -ForegroundColor Magenta
    Write-Host "  Install dir: $InstallDir" -ForegroundColor DarkGray
    Write-Host ""

    Get-Process -Name "ZapretikApp" -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Step "Closing running Zapretik..."
        $_ | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 800
    }

    $src = Get-SourceExe
    Write-Step ("Source: " + $src.From + " -> " + $src.Path)

    Ensure-Dir $InstallDir
    $destExe = Join-Path $InstallDir $ExeName
    Copy-Item -LiteralPath $src.Path -Destination $destExe -Force
    Write-Step "Installed: $destExe"

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

    Write-Step "Creating shortcuts..."
    $desktopLnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "$AppName.lnk"
    $startDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
    Ensure-Dir $startDir
    $startLnk = Join-Path $startDir "$AppName.lnk"

    New-Shortcut $desktopLnk $destExe $AppName
    New-Shortcut $startLnk $destExe $AppName

    Write-Host ""
    Write-Host "  Install complete." -ForegroundColor Green
    Write-Host "  Folder: $InstallDir" -ForegroundColor Gray
    Write-Host ""

    $answer = Read-Host "Launch Zapretik now? (Y/n)"
    if ([string]::IsNullOrWhiteSpace($answer) -or $answer -match '^[YyDd]') {
        Start-Process -FilePath $destExe
    }

    Write-Host ""
    Write-Host "Done." -ForegroundColor DarkGray
    Start-Sleep -Seconds 2
    exit 0
}
catch {
    Write-Host ""
    Write-Host "INSTALL ERROR:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    try { pause } catch { Read-Host "Press Enter to exit" }
    exit 1
}
