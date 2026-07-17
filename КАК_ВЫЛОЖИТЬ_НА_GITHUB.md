# GitHub: exteriya1337 / ZapretikApp

## Быстрый старт (один раз)

### 1. Создай репозиторий
1. Открой: https://github.com/new  
2. Name: **`ZapretikApp`**  
3. Public  
4. **Без** галочек README/license (репо пустой)  
5. Create repository  

### 2. Залей код (PowerShell)

```powershell
cd C:\Users\deepc\Desktop\ZapretikApp
git remote remove origin 2>$null
git remote add origin https://github.com/exteriya1337/ZapretikApp.git
git push -u origin main
```

Войди в GitHub, когда попросит (браузер / token).

### 3. Release v1.0.0
1. https://github.com/exteriya1337/ZapretikApp/releases/new  
2. Tag: **`v1.0.0`**  
3. Title: Zapretik 1.0.0  
4. Приложи файл: `C:\Users\deepc\Desktop\ZapretikApp.exe`  
5. Publish release  

### 4. Проверь latest.json
Файл уже настроен:
- version: `1.0.0`
- url: `https://github.com/exteriya1337/ZapretikApp/releases/download/v1.0.0/ZapretikApp.exe`

После push он будет доступен как:
`https://raw.githubusercontent.com/exteriya1337/ZapretikApp/main/update/latest.json`

## Новая версия (1.0.1 и дальше)

1. В `ZapretikApp\AppVersion.cs` → `Current = "1.0.1"`  
2. Собрать Release  
3. Release на GitHub + exe  
4. Обновить `update/latest.json` (version, url, sha256, notes)  
5. `git add .` → `git commit` → `git push`  

```powershell
Get-FileHash C:\Users\deepc\Desktop\ZapretikApp.exe -Algorithm SHA256
```
