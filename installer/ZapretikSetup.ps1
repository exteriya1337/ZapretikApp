# Zapretik installer — GUI with progress bar
# Installs into %LOCALAPPDATA%\Zapretik
# Optionally refreshes Zapret folder while keeping user settings.
# Encoding: UTF-8 with BOM recommended for PS 5.x Cyrillic
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$AppName     = "Zapretik"
$ExeName     = "ZapretikApp.exe"
$AppVersion  = "1.0.7"
$InstallDir  = Join-Path $env:LOCALAPPDATA "Zapretik"
$ReleaseUrl  = "https://github.com/exteriya1337/ZapretikApp/releases/download/v$AppVersion/ZapretikApp.exe"
$ConfigUrl   = "https://github.com/exteriya1337/ZapretikApp/releases/download/v$AppVersion/ZapretikApp.exe.config"
$RepoUrl     = "https://github.com/exteriya1337/ZapretikApp"
$DriveFolderId = "16a_u33wp4LqphSkUMnuJie8IEb1p_3Aw"
$DriveFolderUrl = "https://drive.google.com/drive/folders/$DriveFolderId"
$UserAgent   = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36"

# Zapret user settings that must survive overwrite
$PreserveRelative = @(
    "utils\game_filter.enabled",
    "utils\check_updates.enabled",
    "lists\ipset-all.txt",
    "lists\ipset-all.txt.backup",
    "lists\ipset-exclude-user.txt",
    "lists\list-general-user.txt",
    "lists\list-exclude-user.txt"
)

# ---------- UI ----------
$form = New-Object System.Windows.Forms.Form
$form.Text = "Zapretik Setup v$AppVersion"
$form.Size = New-Object System.Drawing.Size(520, 420)
$form.StartPosition = "CenterScreen"
$form.FormBorderStyle = "FixedDialog"
$form.MaximizeBox = $false
$form.MinimizeBox = $false
$form.BackColor = [System.Drawing.Color]::FromArgb(43, 43, 46)
$form.ForeColor = [System.Drawing.Color]::FromArgb(240, 240, 242)
$form.Font = New-Object System.Drawing.Font("Segoe UI", 9.5)

$lblTitle = New-Object System.Windows.Forms.Label
$lblTitle.Text = "Установка Zapretik"
$lblTitle.Font = New-Object System.Drawing.Font("Segoe UI", 16, [System.Drawing.FontStyle]::Bold)
$lblTitle.ForeColor = [System.Drawing.Color]::FromArgb(124, 140, 255)
$lblTitle.Location = New-Object System.Drawing.Point(20, 16)
$lblTitle.AutoSize = $true
$form.Controls.Add($lblTitle)

$lblSub = New-Object System.Windows.Forms.Label
$lblSub.Text = "v$AppVersion  ·  $InstallDir"
$lblSub.ForeColor = [System.Drawing.Color]::FromArgb(168, 168, 176)
$lblSub.Location = New-Object System.Drawing.Point(22, 48)
$lblSub.Size = New-Object System.Drawing.Size(460, 20)
$form.Controls.Add($lblSub)

$lblStep = New-Object System.Windows.Forms.Label
$lblStep.Text = "Готов к установке"
$lblStep.Font = New-Object System.Drawing.Font("Segoe UI", 10, [System.Drawing.FontStyle]::Bold)
$lblStep.Location = New-Object System.Drawing.Point(22, 88)
$lblStep.Size = New-Object System.Drawing.Size(460, 22)
$form.Controls.Add($lblStep)

$bar = New-Object System.Windows.Forms.ProgressBar
$bar.Location = New-Object System.Drawing.Point(22, 118)
$bar.Size = New-Object System.Drawing.Size(460, 22)
$bar.Style = "Continuous"
$bar.Minimum = 0
$bar.Maximum = 100
$bar.Value = 0
$form.Controls.Add($bar)

$lblPct = New-Object System.Windows.Forms.Label
$lblPct.Text = "0%"
$lblPct.ForeColor = [System.Drawing.Color]::FromArgb(168, 168, 176)
$lblPct.Location = New-Object System.Drawing.Point(22, 146)
$lblPct.Size = New-Object System.Drawing.Size(100, 18)
$form.Controls.Add($lblPct)

$log = New-Object System.Windows.Forms.TextBox
$log.Multiline = $true
$log.ReadOnly = $true
$log.ScrollBars = "Vertical"
$log.BackColor = [System.Drawing.Color]::FromArgb(35, 35, 38)
$log.ForeColor = [System.Drawing.Color]::FromArgb(200, 200, 208)
$log.BorderStyle = "FixedSingle"
$log.Location = New-Object System.Drawing.Point(22, 172)
$log.Size = New-Object System.Drawing.Size(460, 140)
$log.Font = New-Object System.Drawing.Font("Consolas", 8.5)
$form.Controls.Add($log)

$chkZapret = New-Object System.Windows.Forms.CheckBox
$chkZapret.Text = "Также обновить Zapret (сохранить настройки: IP, Game Filter, user-листы)"
$chkZapret.ForeColor = [System.Drawing.Color]::FromArgb(220, 220, 228)
$chkZapret.Location = New-Object System.Drawing.Point(22, 320)
$chkZapret.Size = New-Object System.Drawing.Size(460, 22)
$chkZapret.Checked = $false
$form.Controls.Add($chkZapret)

$btnInstall = New-Object System.Windows.Forms.Button
$btnInstall.Text = "Установить"
$btnInstall.Size = New-Object System.Drawing.Size(120, 34)
$btnInstall.Location = New-Object System.Drawing.Point(240, 350)
$btnInstall.BackColor = [System.Drawing.Color]::FromArgb(124, 140, 255)
$btnInstall.ForeColor = [System.Drawing.Color]::White
$btnInstall.FlatStyle = "Flat"
$btnInstall.FlatAppearance.BorderSize = 0
$btnInstall.Cursor = [System.Windows.Forms.Cursors]::Hand
$form.Controls.Add($btnInstall)

$btnClose = New-Object System.Windows.Forms.Button
$btnClose.Text = "Закрыть"
$btnClose.Size = New-Object System.Drawing.Size(100, 34)
$btnClose.Location = New-Object System.Drawing.Point(372, 350)
$btnClose.BackColor = [System.Drawing.Color]::FromArgb(62, 62, 68)
$btnClose.ForeColor = [System.Drawing.Color]::White
$btnClose.FlatStyle = "Flat"
$btnClose.FlatAppearance.BorderSize = 0
$btnClose.DialogResult = [System.Windows.Forms.DialogResult]::Cancel
$form.Controls.Add($btnClose)
$form.CancelButton = $btnClose

function Write-Log([string]$msg) {
    $ts = Get-Date -Format "HH:mm:ss"
    $line = "[$ts] $msg"
    if ($log.InvokeRequired) {
        $log.Invoke([Action]{ $log.AppendText($line + [Environment]::NewLine) })
    } else {
        $log.AppendText($line + [Environment]::NewLine)
    }
}

function Set-Progress([int]$pct, [string]$step) {
    if ($pct -lt 0) { $pct = 0 }
    if ($pct -gt 100) { $pct = 100 }
    $action = {
        $bar.Value = $pct
        $lblPct.Text = "$pct%"
        if ($step) { $lblStep.Text = $step }
        [System.Windows.Forms.Application]::DoEvents()
    }
    if ($form.InvokeRequired) { $form.Invoke($action) } else { & $action }
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
        (Join-Path (Split-Path -Parent $here) "ZapretikApp\bin\Release\$ExeName"),
        (Join-Path $env:USERPROFILE "Desktop\$ExeName"),
        (Join-Path ([Environment]::GetFolderPath("Desktop")) $ExeName),
        (Join-Path ([Environment]::GetFolderPath("Desktop")) "Zapretik\$ExeName")
    )
    foreach ($local in $candidates) {
        if ($local -and (Test-Path -LiteralPath $local)) {
            return @{ Path = $local; From = "local" }
        }
    }

    $tmp = Join-Path $env:TEMP "Zapretik_setup_download"
    Ensure-Dir $tmp
    $dest = Join-Path $tmp $ExeName
    Write-Log "Скачивание $ExeName с GitHub Releases..."
    Set-Progress 15 "Скачивание Zapretik..."
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    $wc = New-Object System.Net.WebClient
    $wc.Headers.Add("User-Agent", "ZapretikSetup/$AppVersion")
    try {
        $wc.DownloadFile($ReleaseUrl, $dest)
    } finally {
        $wc.Dispose()
    }
    if (-not (Test-Path -LiteralPath $dest) -or ((Get-Item -LiteralPath $dest).Length -lt 1024)) {
        throw "Скачанный файл пуст или повреждён.`n$ReleaseUrl"
    }
    return @{ Path = $dest; From = "download" }
}

function New-Shortcut([string]$shortcutPath, [string]$targetPath, [string]$description) {
    $wsh = New-Object -ComObject WScript.Shell
    $sc = $wsh.CreateShortcut($shortcutPath)
    $sc.TargetPath = $targetPath
    $sc.WorkingDirectory = Split-Path -Parent $targetPath
    $sc.Description = $description
    $sc.IconLocation = "$targetPath,0"
    $sc.Save()
}

function Get-PreserveList([string]$zapretRoot) {
    $list = New-Object System.Collections.Generic.List[string]
    foreach ($rel in $PreserveRelative) {
        $list.Add($rel)
    }
    $listsDir = Join-Path $zapretRoot "lists"
    if (Test-Path -LiteralPath $listsDir) {
        Get-ChildItem -LiteralPath $listsDir -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '(?i)-user\.txt$' } |
            ForEach-Object {
                $rel = "lists\$($_.Name)"
                if (-not $list.Contains($rel)) { $list.Add($rel) }
            }
    }
    $utilsDir = Join-Path $zapretRoot "utils"
    if (Test-Path -LiteralPath $utilsDir) {
        Get-ChildItem -LiteralPath $utilsDir -File -Filter "*.enabled" -ErrorAction SilentlyContinue |
            ForEach-Object {
                $rel = "utils\$($_.Name)"
                if (-not $list.Contains($rel)) { $list.Add($rel) }
            }
    }
    return $list
}

function Backup-ZapretSettings([string]$zapretRoot) {
    $backupRoot = Join-Path $env:TEMP ("Zapretik_settings_" + [Guid]::NewGuid().ToString("N"))
    Ensure-Dir $backupRoot
    $saved = @()
    foreach ($rel in (Get-PreserveList $zapretRoot)) {
        $src = Join-Path $zapretRoot $rel
        if (Test-Path -LiteralPath $src) {
            $dst = Join-Path $backupRoot $rel
            Ensure-Dir (Split-Path -Parent $dst)
            Copy-Item -LiteralPath $src -Destination $dst -Force
            $saved += $rel
        }
    }
    return @{ Dir = $backupRoot; Files = $saved }
}

function Restore-ZapretSettings([string]$zapretRoot, $backup) {
    if (-not $backup -or -not $backup.Dir) { return 0 }
    $n = 0
    foreach ($rel in $backup.Files) {
        $src = Join-Path $backup.Dir $rel
        $dst = Join-Path $zapretRoot $rel
        if (Test-Path -LiteralPath $src) {
            Ensure-Dir (Split-Path -Parent $dst)
            Copy-Item -LiteralPath $src -Destination $dst -Force
            $n++
        }
    }
    try { Remove-Item -LiteralPath $backup.Dir -Recurse -Force -ErrorAction SilentlyContinue } catch {}
    return $n
}

function Test-IsDriveFolder([string]$raw, [string]$name) {
    if ($raw -match '(?i)folder') { return $true }
    if ($name -match '^(bin|lists|utils)$') { return $true }
    return $false
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

function Get-DriveFolderItems([string]$folderId) {
    $url = "https://drive.google.com/drive/folders/$folderId"
    $req = [System.Net.HttpWebRequest]::Create($url)
    $req.UserAgent = $UserAgent
    $req.AutomaticDecompression = [System.Net.DecompressionMethods]::GZip -bor [System.Net.DecompressionMethods]::Deflate
    $resp = $req.GetResponse()
    $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
    $html = $reader.ReadToEnd()
    $reader.Close(); $resp.Close()

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
                $map[$id] = [pscustomobject]@{ Id = $id; Name = $name; Raw = $raw; IsFolder = (Test-IsDriveFolder $raw $name) }
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
            if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
            $wc = New-Object System.Net.WebClient
            $wc.Headers.Add("User-Agent", $UserAgent)
            try { $wc.DownloadFile($url, $tmp) } finally { $wc.Dispose() }
            if (-not (Test-Path -LiteralPath $tmp)) { continue }
            $len = (Get-Item -LiteralPath $tmp).Length
            $looksHtml = $false
            if ($len -ge 32) {
                $head = Get-Content -LiteralPath $tmp -TotalCount 1 -ErrorAction SilentlyContinue
                if ($head -match '(?i)<!DOCTYPE|<html') { $looksHtml = $true }
            }
            if ($looksHtml) {
                $html = Get-Content -LiteralPath $tmp -Raw -ErrorAction SilentlyContinue
                if ($html -match 'name="uuid"\s+value="([^"]+)"') {
                    $retry = "https://drive.usercontent.google.com/download?id=$fileId&export=download&confirm=t&uuid=$($Matches[1])"
                    $wc2 = New-Object System.Net.WebClient
                    $wc2.Headers.Add("User-Agent", $UserAgent)
                    try { $wc2.DownloadFile($retry, $tmp) } finally { $wc2.Dispose() }
                    $len = (Get-Item -LiteralPath $tmp).Length
                    if ($len -ge 32) {
                        $head = Get-Content -LiteralPath $tmp -TotalCount 1 -ErrorAction SilentlyContinue
                        if ($head -match '(?i)<!DOCTYPE|<html') { continue }
                    }
                } else { continue }
            }
            Ensure-Dir (Split-Path -Parent $destPath)
            if (Test-Path -LiteralPath $destPath) { Remove-Item -LiteralPath $destPath -Force }
            Move-Item -LiteralPath $tmp -Destination $destPath -Force
            return $true
        } catch { }
    }
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue }
    return $false
}

function Collect-DriveJobs([string]$folderId, [string]$destDir, [int]$depth, [System.Collections.ArrayList]$jobs) {
    if ($depth -gt 8) { return }
    Ensure-Dir $destDir
    $items = Get-DriveFolderItems $folderId
    foreach ($item in $items) {
        $safeName = $item.Name -replace '[<>:"/\\|?*]', '_'
        $target = Join-Path $destDir $safeName
        if ($item.IsFolder) {
            Collect-DriveJobs $item.Id $target ($depth + 1) $jobs
        } else {
            [void]$jobs.Add([pscustomobject]@{ Id = $item.Id; Path = $target; Name = $safeName })
        }
    }
}

function Update-ZapretFolder([string]$destRoot, [int]$progressBase, [int]$progressSpan) {
    Ensure-Dir $destRoot
    $hadExisting = Test-Path -LiteralPath (Join-Path $destRoot "bin\winws.exe")
    $backup = $null
    if ($hadExisting) {
        Write-Log "Найден существующий Zapret — сохраняю настройки..."
        $backup = Backup-ZapretSettings $destRoot
        Write-Log ("Сохранено файлов настроек: " + $backup.Files.Count)
        foreach ($f in $backup.Files) { Write-Log "  · $f" }
    } else {
        Write-Log "Загрузка Zapret в новую папку..."
    }

    Set-Progress $progressBase "Список файлов Zapret..."
    $jobs = New-Object System.Collections.ArrayList
    Collect-DriveJobs $DriveFolderId $destRoot 0 $jobs
    if ($jobs.Count -eq 0) {
        throw "Не найдено файлов на Google Drive.`n$DriveFolderUrl"
    }

    $ok = 0; $fail = 0; $i = 0
    foreach ($job in $jobs) {
        $i++
        $pct = $progressBase + [int](($progressSpan * $i) / $jobs.Count)
        Set-Progress $pct ("Zapret: $($job.Name) ($i/$($jobs.Count))")
        if (Download-DriveFile $job.Id $job.Path) { $ok++ } else { $fail++; Write-Log "FAIL: $($job.Name)" }
    }

    if ($backup) {
        $restored = Restore-ZapretSettings $destRoot $backup
        Write-Log "Восстановлено настроек: $restored"
    }

    Write-Log "Zapret: OK=$ok, FAIL=$fail → $destRoot"
    if ($ok -eq 0) { throw "Не удалось скачать Zapret." }
    return @{ Ok = $ok; Fail = $fail; Path = $destRoot; Restored = $(if ($backup) { $backup.Files.Count } else { 0 }) }
}

function Find-ExistingZapretPath {
    $candidates = @(
        (Join-Path ([Environment]::GetFolderPath("Desktop")) "zapret-discord-youtube"),
        (Join-Path $env:USERPROFILE "Desktop\zapret-discord-youtube")
    )
    # From user.config if present
    try {
        $cfgRoot = Join-Path $env:LOCALAPPDATA "ZapretikApp"
        if (Test-Path $cfgRoot) {
            Get-ChildItem $cfgRoot -Recurse -Filter "user.config" -ErrorAction SilentlyContinue | ForEach-Object {
                $xml = [xml](Get-Content $_.FullName -Raw -ErrorAction SilentlyContinue)
                $node = $xml.SelectSingleNode("//setting[@name='ZapretRootPath']/value")
                if ($node -and $node.InnerText) { $candidates += $node.InnerText.Trim() }
            }
        }
    } catch {}

    foreach ($p in $candidates) {
        if ($p -and (Test-Path -LiteralPath (Join-Path $p "bin\winws.exe"))) { return $p }
        if ($p -and (Test-Path -LiteralPath (Join-Path $p "service.bat"))) { return $p }
    }
    return (Join-Path ([Environment]::GetFolderPath("Desktop")) "zapret-discord-youtube")
}

# Pre-check: if zapret exists, pre-check the box
$existingZapret = Find-ExistingZapretPath
if (Test-Path -LiteralPath (Join-Path $existingZapret "bin\winws.exe")) {
    $chkZapret.Checked = $true
    $chkZapret.Text = "Обновить Zapret (найден: $existingZapret) — настройки сохранятся"
}

$btnInstall.Add_Click({
    $btnInstall.Enabled = $false
    $chkZapret.Enabled = $false
    $updateZapret = $chkZapret.Checked

    $worker = {
        param($updateZapretFlag, $zapretPath)
        try {
            [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        } catch {}

        try {
            Set-Progress 2 "Закрытие Zapretik..."
            Write-Log "Установка Zapretik v$AppVersion"
            Write-Log "Каталог: $InstallDir"
            Write-Log "Репозиторий: $RepoUrl"
            Write-Log "Автообновления: встроены (GitHub Releases + latest.json)"

            Get-Process -Name "ZapretikApp" -ErrorAction SilentlyContinue | ForEach-Object {
                Write-Log "Закрываю процесс PID $($_.Id)..."
                $_ | Stop-Process -Force -ErrorAction SilentlyContinue
            }
            Start-Sleep -Milliseconds 600

            Set-Progress 10 "Поиск / скачивание exe..."
            $src = Get-SourceExe
            Write-Log ("Источник: " + $src.From + " → " + $src.Path)

            Set-Progress 40 "Копирование в LocalAppData..."
            Ensure-Dir $InstallDir
            $destExe = Join-Path $InstallDir $ExeName
            Copy-Item -LiteralPath $src.Path -Destination $destExe -Force
            Write-Log "Установлен: $destExe"

            $srcConfig = $src.Path + ".config"
            $destConfig = $destExe + ".config"
            if (Test-Path -LiteralPath $srcConfig) {
                Copy-Item -LiteralPath $srcConfig -Destination $destConfig -Force
                Write-Log "Скопирован .config"
            } else {
                try {
                    $wc = New-Object System.Net.WebClient
                    $wc.Headers.Add("User-Agent", "ZapretikSetup/$AppVersion")
                    try { $wc.DownloadFile($ConfigUrl, $destConfig) } catch {} finally { $wc.Dispose() }
                } catch {}
            }

            Set-Progress 55 "Ярлыки..."
            $desktopLnk = Join-Path ([Environment]::GetFolderPath("Desktop")) "$AppName.lnk"
            $startDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
            Ensure-Dir $startDir
            $startLnk = Join-Path $startDir "$AppName.lnk"
            New-Shortcut $desktopLnk $destExe $AppName
            New-Shortcut $startLnk $destExe $AppName
            Write-Log "Ярлыки: рабочий стол + меню Пуск"

            $vi = (Get-Item $destExe).VersionInfo
            Write-Log ("Версия файла: " + $vi.FileVersion + " / " + $vi.ProductName)

            if ($updateZapretFlag) {
                Write-Log "——— Обновление Zapret ———"
                Set-Progress 60 "Обновление Zapret..."
                $zr = Update-ZapretFolder $zapretPath 60 35
                Write-Log "Zapret готов. Настройки сохранены: $($zr.Restored)"
            }

            Set-Progress 100 "Готово"
            Write-Log ""
            Write-Log "Установка завершена успешно."
            Write-Log "Zapretik: $destExe"
            if ($updateZapretFlag) { Write-Log "Zapret: $zapretPath" }
            Write-Log "Обновления приложения: Настройки → Обновления (или автоматически при запуске)."

            $form.Invoke([Action]{
                $btnClose.Text = "Готово"
                $r = [System.Windows.Forms.MessageBox]::Show(
                    $form,
                    "Zapretik v$AppVersion установлен.`n`nПапка:`n$InstallDir`n`nЗапустить сейчас?",
                    "Zapretik Setup",
                    [System.Windows.Forms.MessageBoxButtons]::YesNo,
                    [System.Windows.Forms.MessageBoxIcon]::Information)
                if ($r -eq [System.Windows.Forms.DialogResult]::Yes) {
                    Start-Process -FilePath $destExe
                }
            })
        }
        catch {
            $err = $_.Exception.Message
            Write-Log "ОШИБКА: $err"
            Set-Progress 0 "Ошибка установки"
            $form.Invoke([Action]{
                [System.Windows.Forms.MessageBox]::Show($form, $err, "Ошибка установки", "OK", "Error") | Out-Null
                $btnInstall.Enabled = $true
                $chkZapret.Enabled = $true
            })
        }
    }

    # Run on background runspace so UI stays responsive
    $ps = [PowerShell]::Create()
    [void]$ps.AddScript($worker.ToString()).AddArgument($updateZapret).AddArgument($existingZapret)
    # Inject functions into runspace — simpler: run sync on UI thread with DoEvents

    # Simpler approach: run on UI thread with Application.DoEvents in progress
    try {
        & $worker $updateZapret $existingZapret
    } catch {
        Write-Log "ОШИБКА: $($_.Exception.Message)"
        [System.Windows.Forms.MessageBox]::Show($form, $_.Exception.Message, "Ошибка", "OK", "Error") | Out-Null
        $btnInstall.Enabled = $true
        $chkZapret.Enabled = $true
    }
})

Write-Log "Zapretik Setup v$AppVersion"
Write-Log "Установка в: $InstallDir"
Write-Log "Автообновление приложения: GitHub Releases + update/latest.json"
if (Test-Path -LiteralPath (Join-Path $existingZapret "bin\winws.exe")) {
    Write-Log "Обнаружен Zapret: $existingZapret"
} else {
    Write-Log "Zapret не найден (можно скачать при установке)."
}
Write-Log "Нажмите «Установить»."

[void]$form.ShowDialog()
exit 0
