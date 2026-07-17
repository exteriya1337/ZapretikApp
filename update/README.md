# Канал обновлений Zapretik

Приложение ищет обновления так:

1. **GitHub Releases API** (`/releases/latest`) — основной источник версии и URL exe  
2. **`update/latest.json`** с зеркал (для SHA256 и запасного канала):
   - GitHub Contents API  
   - `releases/latest/download/latest.json` (если приложили к релизу)  
   - raw.githubusercontent.com  
   - jsDelivr / Fastly (**последние** — `@main` часто отстаёт на часы)

Выбирается **максимальная** версия среди всех ответов; поля (url / sha256 / notes) склеиваются.

В UI: кнопка **«Обновления»** — ручная проверка с текстом ошибки, если сеть недоступна.

## Формат `latest.json`

```json
{
  "version": "1.0.5",
  "url": "https://github.com/exteriya1337/ZapretikApp/releases/download/v1.0.5/ZapretikApp.exe",
  "sha256": "хеш_sha256_от_exe_в_нижнем_регистре",
  "notes": "Что нового (показывается в окне обновления)"
}
```

## Как выкатить обновление

1. Подними `AppVersion.Current` и `AssemblyVersion` / `AssemblyFileVersion` (например `1.0.5`).
2. Собери Release: `ZapretikApp\bin\Release\ZapretikApp.exe`
3. Посчитай SHA256:
   ```powershell
   Get-FileHash .\ZapretikApp.exe -Algorithm SHA256
   ```
4. Обнови `update/latest.json` (version, url, sha256, notes).
5. Закоммить и запушь в `main`.
6. Создай GitHub Release `vX.Y.Z` (заголовок: `Zapretik X.Y.Z`), приложи **только**:
   - `ZapretikApp.exe` — приложение + цель автообновления
   - `latest.json` — фид обновлений (SHA256 и notes)
   - `Zapretik_Installer.zip` — `ZapretikSetup.bat` + `ZapretikSetup.ps1`
7. Сбрось кэш jsDelivr:
   ```
   https://purge.jsdelivr.net/gh/exteriya1337/ZapretikApp@main/update/latest.json
   ```
8. Проверь Releases API и raw `latest.json`.

## Локальная папка (Desktop)

```
Zapretik_Release/
  installer/
    ZapretikSetup.bat   ← запуск (скачает exe с GitHub, если его нет рядом)
    ZapretikSetup.ps1
```

Инсталлятор: локальный exe (рядом / на уровень выше) или download с Release.

## Клиенты

| Версия | Поведение |
|--------|-----------|
| **≤1.0.2** | Может не увидеть обновление из‑за stale CDN — ставить вручную. |
| **1.0.3+** | max(version) по зеркалам. |
| **1.0.5+** | Releases API + зеркала + кнопка «Обновления». |
