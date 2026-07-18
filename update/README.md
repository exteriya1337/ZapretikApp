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
  "version": "1.0.7",
  "url": "https://github.com/exteriya1337/ZapretikApp/releases/download/v1.0.7/ZapretikApp.exe",
  "sha256": "хеш_sha256_от_exe_в_нижнем_регистре",
  "notes": "Что нового (показывается в окне обновления)"
}
```

## Как выкатить обновление

1. Подними `AppVersion.Current` и `AssemblyVersion` / `AssemblyFileVersion` (например `1.0.7`).
2. Собери Release: `ZapretikApp\bin\Release\ZapretikApp.exe`
3. Посчитай SHA256:
   ```powershell
   Get-FileHash .\ZapretikApp.exe -Algorithm SHA256
   ```
4. Обнови `update/latest.json` (version, url, sha256, notes).
5. Закоммить и запушь в `main`.
6. Создай GitHub Release `v1.0.7`, приложи:
   - `ZapretikApp.exe`
   - `ZapretikApp.exe.config` (если есть)
   - **`latest.json`** (копия из `update/`) — чтобы `releases/latest/download/latest.json` работал
   - `Zapretik_Installer.zip` (для новых пользователей)
7. Сбрось кэш jsDelivr (иначе старые клиенты могут видеть старый latest.json):
   ```
   https://purge.jsdelivr.net/gh/exteriya1337/ZapretikApp@main/update/latest.json
   ```
8. Проверь:
   - raw: `https://raw.githubusercontent.com/exteriya1337/ZapretikApp/main/update/latest.json`
   - API: `https://api.github.com/repos/exteriya1337/ZapretikApp/releases/latest`
   - jsDelivr: `https://cdn.jsdelivr.net/gh/exteriya1337/ZapretikApp@main/update/latest.json`

## Важно про старые клиенты

| Версия клиента | Поведение |
|----------------|-----------|
| **1.0.2** | Берёт **первое** живое зеркало (часто jsDelivr). Если CDN отдаёт старый JSON ≤ 1.0.2 — обновление **не предложит**. Нужна ручная установка. |
| **1.0.3+** | Выбирает max(version) по зеркалам. |
| **1.0.5+** | Releases API + max по зеркалам + кнопка «Обновления» + зеркала скачивания. |
