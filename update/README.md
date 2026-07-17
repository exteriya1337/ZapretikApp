# Канал обновлений Zapretik

Файл `latest.json` читает приложение при **обычном** запуске (не `--tray`).

## Формат

```json
{
  "version": "1.0.1",
  "url": "https://github.com/ВЛАДЕЛЕЦ/ZapretikApp/releases/download/v1.0.1/ZapretikApp.exe",
  "sha256": "хеш_sha256_от_exe_в_нижнем_регистре",
  "notes": "Что нового (показывается в окне обновления)"
}
```

## Как выкатить обновление

1. Подними `AppVersion.Current` в коде (например `1.0.1`).
2. Собери Release: `ZapretikApp\bin\Release\ZapretikApp.exe`
3. Посчитай SHA256:
   ```powershell
   Get-FileHash .\ZapretikApp.exe -Algorithm SHA256
   ```
4. Создай GitHub Release `v1.0.1`, приложи `ZapretikApp.exe`.
5. Обнови `update/latest.json` (version, url, sha256, notes) и залей в `main`.

URL в `AppVersion.UpdateManifestUrl` должен указывать на raw-файл этого `latest.json`.
