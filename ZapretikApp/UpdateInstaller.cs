using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace ZapretikApp
{
    /// <summary>
    /// Downloads a new build and launches a batch updater that replaces the running exe.
    /// </summary>
    internal static class UpdateInstaller
    {
        public static void DownloadAndApply(UpdateInfo info, Action<string> progress)
        {
            if (info == null || string.IsNullOrWhiteSpace(info.Url))
                throw new InvalidOperationException("Нет URL обновления.");

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }

            var targetExe = AutostartHelper.GetExecutablePath();
            if (string.IsNullOrEmpty(targetExe) || !File.Exists(targetExe))
                throw new InvalidOperationException("Не удалось определить путь к текущему exe.");

            var workDir = Path.Combine(Path.GetTempPath(), "Zapretik", "update");
            Directory.CreateDirectory(workDir);

            var downloadPath = Path.Combine(workDir, "ZapretikApp_new.exe");
            var updaterPath = Path.Combine(workDir, "apply_update.cmd");

            if (File.Exists(downloadPath))
            {
                try { File.Delete(downloadPath); } catch { }
            }

            if (progress != null)
                progress("Скачивание " + info.Version + "…");

            DownloadFile(info.Url, downloadPath);

            if (!File.Exists(downloadPath) || new FileInfo(downloadPath).Length < 1024)
                throw new InvalidOperationException("Скачанный файл пуст или слишком маленький.");

            if (!string.IsNullOrWhiteSpace(info.Sha256))
            {
                if (progress != null)
                    progress("Проверка SHA256…");
                var hash = ComputeSha256(downloadPath);
                if (!string.Equals(hash, info.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("SHA256 не совпал. Файл повреждён или подменён.");
            }

            if (progress != null)
                progress("Подготовка обновления…");

            WriteUpdaterScript(updaterPath, downloadPath, targetExe);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/C \"" + updaterPath + "\"",
                WorkingDirectory = workDir,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(psi);
        }

        private static void DownloadFile(string url, string destPath)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 120000;
            request.ReadWriteTimeout = 120000;
            request.UserAgent = "ZapretikApp/" + AppVersion.Current;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var input = response.GetResponseStream())
            using (var output = File.Create(destPath))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    output.Write(buffer, 0, read);
            }
        }

        private static string ComputeSha256(string path)
        {
            using (var fs = File.OpenRead(path))
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(fs);
                var sb = new StringBuilder(hash.Length * 2);
                foreach (var b in hash)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        private static void WriteUpdaterScript(string scriptPath, string newExe, string targetExe)
        {
            // Wait for process exit, replace files, restart.
            var targetDir = Path.GetDirectoryName(targetExe) ?? ".";
            var configSrc = newExe + ".config"; // optional
            var configDst = targetExe + ".config";

            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 65001 >nul");
            sb.AppendLine("setlocal EnableDelayedExpansion");
            sb.AppendLine("set \"NEW=" + newExe + "\"");
            sb.AppendLine("set \"TARGET=" + targetExe + "\"");
            sb.AppendLine("rem wait until Zapretik exits (max ~40s)");
            sb.AppendLine("set /a n=0");
            sb.AppendLine(":wait");
            sb.AppendLine("tasklist /FI \"IMAGENAME eq ZapretikApp.exe\" | find /I \"ZapretikApp.exe\" >nul");
            sb.AppendLine("if not errorlevel 1 (");
            sb.AppendLine("  timeout /t 1 /nobreak >nul");
            sb.AppendLine("  set /a n+=1");
            sb.AppendLine("  if !n! LSS 40 goto wait");
            sb.AppendLine(")");
            sb.AppendLine("timeout /t 1 /nobreak >nul");
            sb.AppendLine("copy /Y \"%NEW%\" \"%TARGET%\" >nul");
            sb.AppendLine("if errorlevel 1 (");
            sb.AppendLine("  ping -n 2 127.0.0.1 >nul");
            sb.AppendLine("  copy /Y \"%NEW%\" \"%TARGET%\" >nul");
            sb.AppendLine(")");
            sb.AppendLine("if exist \"%NEW%.config\" copy /Y \"%NEW%.config\" \"%TARGET%.config\" >nul");
            sb.AppendLine("start \"\" \"%TARGET%\"");
            sb.AppendLine("del /f /q \"%~f0\" >nul 2>&1");
            sb.AppendLine("exit /b 0");

            File.WriteAllText(scriptPath, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
