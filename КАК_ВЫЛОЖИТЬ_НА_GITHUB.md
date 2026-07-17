# Как создать репозиторий и включить автообновление

На этом ПК **нет GitHub CLI (`gh`) и нет входа в GitHub**, поэтому репозиторий нужно создать один раз вручную (5 минут). Код автообновления в приложении **уже есть**.

## 1. Создай репозиторий на GitHub

1. Зайди на https://github.com/new  
2. **Repository name:** `ZapretikApp`  
3. Public (чтобы raw-ссылки работали без токена)  
4. **Create repository** (без README, если будешь пушить эту папку)

Запомни свой логин, например: `ivanov`  
Тогда raw-URL будет:
`https://raw.githubusercontent.com/ivanov/ZapretikApp/main/update/latest.json`

## 2. Поправь URL в приложении (если логин не deepc-dev)

Открой файл:
`ZapretikApp\AppVersion.cs`

Строка:
```csharp
public const string UpdateManifestUrl =
    "https://raw.githubusercontent.com/deepc-dev/ZapretikApp/main/update/latest.json";
```

Замени `deepc-dev` на **свой логин GitHub**.

## 3. Залей проект в GitHub

Открой PowerShell в папке `C:\Users\deepc\Desktop\ZapretikApp`:

```powershell
cd C:\Users\deepc\Desktop\ZapretikApp
git init
git add .
git commit -m "Zapretik with auto-update"
git branch -M main
git remote add origin https://github.com/ТВОЙ_ЛОГИН/ZapretikApp.git
git push -u origin main
```

GitHub попросит войти (браузер или Personal Access Token).

## 4. Выложи первый Release (exe)

1. Собери Release (или возьми exe с рабочего стола).  
2. GitHub → твой репозиторий → **Releases** → **Create a new release**  
3. Tag: `v1.0.0`  
4. Приложи файл `ZapretikApp.exe`  
5. Publish  

## 5. Обнови latest.json

В `update/latest.json` пропиши:

- `version`: `1.0.0` (как в приложении сейчас)  
- `url`: ссылка на exe из Releases  
- `sha256`: из PowerShell:

```powershell
Get-FileHash C:\Users\deepc\Desktop\ZapretikApp.exe -Algorithm SHA256
```

Закоммить и push:

```powershell
git add update/latest.json
git commit -m "update manifest 1.0.0"
git push
```

## 6. Как выкатывать 1.0.1, 1.0.2…

1. В `AppVersion.cs` → `Current = "1.0.1"`  
2. Собрать Release  
3. Release на GitHub `v1.0.1` + exe  
4. `latest.json` → version/url/sha256/notes  
5. `git push`  

У пользователей со **старой** версией при **обычном** запуске (не из трея) всплывёт окно обновления.

## Поведение приложения

| Запуск | Обновление |
|--------|------------|
| Двойной клик по exe | Проверка + плашка, если есть новая версия |
| Автозапуск `--tray` | Без плашки (тихий фон) |
| В title bar | `v1.0.0` (версия) |
