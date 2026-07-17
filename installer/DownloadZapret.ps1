# Download Zapret distribution from a public Google Drive folder.
# Encoding: ASCII/UTF-8 (no Cyrillic) for Windows PowerShell 5.x safety.
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Public folder: https://drive.google.com/drive/folders/16a_u33wp4LqphSkUMnuJie8IEb1p_3Aw
$FolderId = "16a_u33wp4LqphSkUMnuJie8IEb1p_3Aw"
$FolderUrl = "https://drive.google.com/drive/folders/$FolderId"
$UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

function Write-Step([string]$msg) {
    Write-Host ""
    Write-Host "==> $msg" -ForegroundColor Cyan
}

function Ensure-Dir([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) {
        New-Item -ItemType Directory -Path $path -Force | Out-Null
    }
}

function Clean-DriveName([string]$raw) {
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    $n = $raw.Trim()
    $n = $n -replace '\s+Shared folder\s*$', ''
    $n = $n -replace '\s+folder\s*$', ''
    $n = $n -replace '\s+Unknown\s*$', ''
    $n = $n -replace '\s+Binary\s*$', ''
    $n = $n -replace '\s+Text\s*$', ''
    $n = $n -replace '\s+Document\s*$', ''
    $n = $n -replace '\s+Shared\s*$', ''
    return $n.Trim()
}

function Test-IsDriveFolder([string]$raw, [string]$name) {
    if ($raw -match '(?i)folder') { return $true }
    if ($name -match '^(bin|lists|utils)$') { return $true }
    return $false
}

function Get-DriveFolderItems([string]$folderId) {
    $url = "https://drive.google.com/drive/folders/$folderId"
    $html = (Invoke-WebRequest -Uri $url -UseBasicParsing -UserAgent $UserAgent).Content
    $map = @{}

    $pairPatterns = @(
        @{ Pattern = 'data-id="([a-zA-Z0-9_-]+)"[^>]*data-tooltip="([^"]+)"'; IdGroup = 1; NameGroup = 2 },
        @{ Pattern = 'data-tooltip="([^"]+)"[^>]*data-id="([a-zA-Z0-9_-]+)"'; IdGroup = 2; NameGroup = 1 },
        @{ Pattern = 'data-id="([a-zA-Z0-9_-]+)"[^>]*aria-label="([^"]+)"'; IdGroup = 1; NameGroup = 2 },
        @{ Pattern = 'aria-label="([^"]+)"[^>]*data-id="([a-zA-Z0-9_-]+)"'; IdGroup = 2; NameGroup = 1 }
    )

    foreach ($p in $pairPatterns) {
        foreach ($m in [regex]::Matches($html, $p.Pattern)) {
            $id = $m.Groups[$p.IdGroup].Value
            $raw = $m.Groups[$p.NameGroup].Value
            if ([string]::IsNullOrWhiteSpace($id) -or $id -eq $folderId) { continue }
            $name = Clean-DriveName $raw
            if ([string]::IsNullOrWhiteSpace($name)) { continue }
            if (-not $map.ContainsKey($id)) {
                $map[$id] = [pscustomobject]@{
                    Id       = $id
                    Name     = $name
                    Raw      = $raw
                    IsFolder = (Test-IsDriveFolder $raw $name)
                }
            }
        }
    }

    return @($map.Values | Sort-Object Name)
}

function Download-DriveFile([string]$fileId, [string]$destPath) {
    $urls = @(
        "https://drive.usercontent.google.com/download?id=$fileId&export=download&confirm=t",
        "https://drive.google.com/uc?export=download&confirm=t&id=$fileId"
    )

    $tmp = $destPath + ".partial"
    foreach ($url in $urls) {
        try {
            if (Test-Path -LiteralPath $tmp) {
                Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
            }

            Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing -UserAgent $UserAgent -MaximumRedirection 5

            if (-not (Test-Path -LiteralPath $tmp)) { continue }
            $len = (Get-Item -LiteralPath $tmp).Length

            # Google virus-scan / error pages are HTML (usually >1 KB). Real flag files may be 0–few bytes.
            $looksHtml = $false
            if ($len -ge 32) {
                $head = Get-Content -LiteralPath $tmp -TotalCount 1 -ErrorAction SilentlyContinue
                if ($head -match '(?i)<!DOCTYPE|<html') {
                    $looksHtml = $true
                }
            }

            if ($looksHtml) {
                $html = Get-Content -LiteralPath $tmp -Raw -ErrorAction SilentlyContinue
                $uuid = $null
                if ($html -match 'name="uuid"\s+value="([^"]+)"') {
                    $uuid = $Matches[1]
                }
                if (-not $uuid) { continue }

                $retry = "https://drive.usercontent.google.com/download?id=$fileId&export=download&confirm=t&uuid=$uuid"
                Invoke-WebRequest -Uri $retry -OutFile $tmp -UseBasicParsing -UserAgent $UserAgent -MaximumRedirection 5
                $len = (Get-Item -LiteralPath $tmp).Length
                if ($len -ge 32) {
                    $head = Get-Content -LiteralPath $tmp -TotalCount 1 -ErrorAction SilentlyContinue
                    if ($head -match '(?i)<!DOCTYPE|<html') { continue }
                }
            }

            $parent = Split-Path -Parent $destPath
            Ensure-Dir $parent
            if (Test-Path -LiteralPath $destPath) {
                Remove-Item -LiteralPath $destPath -Force
            }
            Move-Item -LiteralPath $tmp -Destination $destPath -Force
            return $true
        }
        catch {
            # try next URL
        }
    }

    if (Test-Path -LiteralPath $tmp) {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
    return $false
}

function Download-DriveFolder([string]$folderId, [string]$destDir, [int]$depth = 0) {
    if ($depth -gt 8) {
        Write-Host "  Skip deep folder: $destDir" -ForegroundColor Yellow
        return @{ Ok = 0; Fail = 0 }
    }

    Ensure-Dir $destDir
    $items = Get-DriveFolderItems $folderId
    $ok = 0
    $fail = 0

    if ($items.Count -eq 0) {
        Write-Host "  (empty or blocked listing: $folderId)" -ForegroundColor Yellow
        return @{ Ok = 0; Fail = 0 }
    }

    foreach ($item in $items) {
        $safeName = $item.Name -replace '[<>:"/\\|?*]', '_'
        $target = Join-Path $destDir $safeName

        if ($item.IsFolder) {
            Write-Host ("  [dir]  " + $safeName) -ForegroundColor DarkGray
            $sub = Download-DriveFolder $item.Id $target ($depth + 1)
            $ok += $sub.Ok
            $fail += $sub.Fail
            continue
        }

        Write-Host ("  [file] " + $safeName) -NoNewline
        if (Download-DriveFile $item.Id $target) {
            $size = (Get-Item -LiteralPath $target).Length
            Write-Host ("  OK  (" + $size + " bytes)") -ForegroundColor Green
            $ok++
        }
        else {
            Write-Host "  FAIL" -ForegroundColor Red
            $fail++
        }
    }

    return @{ Ok = $ok; Fail = $fail }
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
}
catch { }

try {
    Write-Host ""
    Write-Host "  Zapret downloader (Google Drive)" -ForegroundColor Magenta
    Write-Host "  Source: $FolderUrl" -ForegroundColor DarkGray
    Write-Host ""

    $desktop = [Environment]::GetFolderPath("Desktop")
    $defaultDest = Join-Path $desktop "zapret-discord-youtube"

    # Optional: DownloadZapret.ps1 -OutDir "D:\path"
    $destRoot = $null
    for ($i = 0; $i -lt $args.Count; $i++) {
        if ($args[$i] -eq "-OutDir" -and ($i + 1) -lt $args.Count) {
            $destRoot = [string]$args[$i + 1]
        }
    }

    if ([string]::IsNullOrWhiteSpace($destRoot)) {
        Write-Host "Where to save Zapret?"
        Write-Host "  Enter = $defaultDest"
        $answer = Read-Host "Path"
        if ([string]::IsNullOrWhiteSpace($answer)) {
            $destRoot = $defaultDest
        }
        else {
            $destRoot = $answer.Trim().Trim('"')
        }
    }

    Write-Step "Destination: $destRoot"
    Ensure-Dir $destRoot

    Write-Step "Downloading from Google Drive..."
    $result = Download-DriveFolder $FolderId $destRoot 0

    Write-Host ""
    $color = if ($result.Fail -eq 0) { "Green" } else { "Yellow" }
    Write-Host ("  Done: OK={0}, FAIL={1}" -f $result.Ok, $result.Fail) -ForegroundColor $color
    Write-Host "  $destRoot" -ForegroundColor Gray

    $winws = Join-Path $destRoot "bin\winws.exe"
    $service = Join-Path $destRoot "service.bat"
    if (-not (Test-Path -LiteralPath $winws)) {
        Write-Host ""
        Write-Host "  WARNING: bin\winws.exe not found. Check folder / Drive access." -ForegroundColor Yellow
    }
    if (-not (Test-Path -LiteralPath $service)) {
        Write-Host "  WARNING: service.bat not found." -ForegroundColor Yellow
    }

    if ($result.Ok -eq 0) {
        throw "No files downloaded. Open manually: $FolderUrl"
    }

    Write-Host ""
    if (-not ($args -contains "-NoPrompt")) {
        $open = Read-Host "Open folder in Explorer? (Y/n)"
        if ([string]::IsNullOrWhiteSpace($open) -or $open -match '^[YyDd]') {
            Start-Process explorer.exe -ArgumentList $destRoot
        }
    }

    Write-Host ""
    Write-Host "Next in Zapretik: Browse... and select this folder." -ForegroundColor Cyan
    Write-Host ""
    Start-Sleep -Seconds 2
    exit 0
}
catch {
    Write-Host ""
    Write-Host "DOWNLOAD ERROR:" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Manual download:" -ForegroundColor DarkGray
    Write-Host "  $FolderUrl" -ForegroundColor DarkGray
    Write-Host ""
    if (-not ($args -contains "-NoPrompt")) {
        try { pause } catch { Read-Host "Press Enter to exit" }
    }
    exit 1
}
