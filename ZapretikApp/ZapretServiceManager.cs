using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.ComponentModel;

namespace ZapretikApp
{
    /// <summary>
    /// Install/remove Zapret Windows service the same way Flowseal service.bat does (menu 1 / 2).
    /// </summary>
    internal static class ZapretServiceManager
    {
        private static readonly HashSet<string> ArgsWithValue = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sni", "host", "altorder"
        };

        public static bool IsServiceBat(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;
            return fileName.StartsWith("service", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>service.bat option 2</summary>
        public static void RemoveServices(string zapretRoot)
        {
            RunElevatedCmd(BuildRemoveScript(), zapretRoot, "Снятие службы zapret");
        }

        /// <summary>service.bat option 1 (always replaces existing service)</summary>
        public static void InstallStrategy(string zapretRoot, string strategyBatFullPath)
        {
            if (string.IsNullOrWhiteSpace(zapretRoot) || !Directory.Exists(zapretRoot))
                throw new InvalidOperationException("Корневая папка Zapret не найдена.");

            if (string.IsNullOrWhiteSpace(strategyBatFullPath) || !File.Exists(strategyBatFullPath))
                throw new InvalidOperationException("Файл стратегии не найден.");

            if (IsServiceBat(Path.GetFileName(strategyBatFullPath)))
                throw new InvalidOperationException("service.bat нельзя устанавливать как стратегию.");

            var binDir = Path.Combine(zapretRoot, "bin") + Path.DirectorySeparatorChar;
            var listsDir = Path.Combine(zapretRoot, "lists") + Path.DirectorySeparatorChar;
            var winws = Path.Combine(binDir, "winws.exe");

            if (!File.Exists(winws))
                throw new InvalidOperationException("Не найден bin\\winws.exe в папке Zapret.");

            var game = LoadGameFilter(zapretRoot);
            var args = ParseWinwsArguments(strategyBatFullPath, zapretRoot, binDir, listsDir, game);
            if (string.IsNullOrWhiteSpace(args))
            {
                throw new InvalidOperationException(
                    "Не удалось разобрать аргументы winws в файле:\n" + Path.GetFileName(strategyBatFullPath));
            }

            // service.bat stores %%~nF (name without extension)
            var strategyName = Path.GetFileNameWithoutExtension(strategyBatFullPath);
            RunElevatedCmd(BuildInstallScript(winws, args, strategyName), zapretRoot, "Установка стратегии zapret");
        }

        private static GameFilterValues LoadGameFilter(string zapretRoot)
        {
            var result = new GameFilterValues
            {
                GameFilter = "12",
                GameFilterTcp = "12",
                GameFilterUdp = "12"
            };

            var flagFile = Path.Combine(zapretRoot, "utils", "game_filter.enabled");
            if (!File.Exists(flagFile))
                return result;

            string mode = null;
            try
            {
                foreach (var line in File.ReadAllLines(flagFile))
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        mode = line.Trim();
                        break;
                    }
                }
            }
            catch
            {
                return result;
            }

            if (string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
            {
                result.GameFilter = result.GameFilterTcp = result.GameFilterUdp = "1024-65535";
            }
            else if (string.Equals(mode, "tcp", StringComparison.OrdinalIgnoreCase))
            {
                result.GameFilter = "1024-65535";
                result.GameFilterTcp = "1024-65535";
                result.GameFilterUdp = "12";
            }
            else if (string.Equals(mode, "udp", StringComparison.OrdinalIgnoreCase))
            {
                result.GameFilter = "1024-65535";
                result.GameFilterTcp = "12";
                result.GameFilterUdp = "1024-65535";
            }

            return result;
        }

        internal static string ParseWinwsArguments(
            string strategyBatPath,
            string zapretRoot,
            string binDir,
            string listsDir,
            GameFilterValues game)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(strategyBatPath);
            }
            catch
            {
                return string.Empty;
            }

            var capture = false;
            var args = new StringBuilder();
            var mergeargs = 0;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                // line continuations in bat
                if (line.EndsWith("^", StringComparison.Ordinal))
                    line = line.Substring(0, line.Length - 1).TrimEnd();

                line = line.Trim();
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                if (line.StartsWith("::", StringComparison.Ordinal) ||
                    line.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Expand game filter placeholders anywhere in the line (as service.bat env does)
                line = line.Replace("%GameFilterTCP%", game.GameFilterTcp);
                line = line.Replace("%GameFilterUDP%", game.GameFilterUdp);
                line = line.Replace("%GameFilter%", game.GameFilter);
                line = line.Replace("%BIN%", binDir);
                line = line.Replace("%LISTS%", listsDir);
                line = line.Replace("%~dp0", zapretRoot.TrimEnd('\\', '/') + "\\");

                var hasWinws = line.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase) >= 0;
                if (hasWinws)
                {
                    capture = true;
                    var idx = line.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
                    var after = idx + "winws.exe".Length;
                    if (after < line.Length && line[after] == '"')
                        after++;
                    line = after < line.Length ? line.Substring(after).Trim() : string.Empty;
                }

                if (!capture || string.IsNullOrWhiteSpace(line))
                    continue;

                // Drop "start" window title prefix if still present on same line
                // (already stripped when winws is mid-line after /min)

                var tokens = TokenizeCmdLine(line);
                var tempArgs = new StringBuilder();

                foreach (var token in tokens)
                {
                    var arg = token;
                    if (string.IsNullOrEmpty(arg) || arg == "^")
                        continue;

                    // skip start command leftovers if any
                    if (arg.Equals("start", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("/min", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("/b", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (arg.StartsWith("--", StringComparison.Ordinal) && mergeargs != 0)
                        mergeargs = 0;

                    var wasQuoted = arg.Length >= 2 && arg[0] == '"' && arg[arg.Length - 1] == '"';
                    if (wasQuoted)
                        arg = arg.Substring(1, arg.Length - 2);

                    arg = ExpandPathArg(arg, zapretRoot, binDir, listsDir, wasQuoted);

                    if (mergeargs == 1)
                    {
                        tempArgs.Append(',');
                        tempArgs.Append(arg);
                    }
                    else if (mergeargs == 3)
                    {
                        tempArgs.Append('=');
                        tempArgs.Append(arg);
                        mergeargs = 1;
                    }
                    else
                    {
                        if (tempArgs.Length > 0)
                            tempArgs.Append(' ');
                        tempArgs.Append(arg);
                    }

                    if (arg.StartsWith("--", StringComparison.Ordinal))
                    {
                        mergeargs = 2;
                    }
                    else if (mergeargs >= 1)
                    {
                        if (mergeargs == 2)
                            mergeargs = 1;
                        if (ArgsWithValue.Contains(arg))
                            mergeargs = 3;
                    }
                }

                if (tempArgs.Length > 0)
                {
                    if (args.Length > 0)
                        args.Append(' ');
                    args.Append(tempArgs);
                }
            }

            return args.ToString().Trim();
        }

        private static string ExpandPathArg(
            string arg,
            string zapretRoot,
            string binDir,
            string listsDir,
            bool wasQuoted)
        {
            if (arg.StartsWith("@", StringComparison.Ordinal))
            {
                var rest = arg.Substring(1);
                if (!Path.IsPathRooted(rest))
                    rest = Path.Combine(zapretRoot, rest.TrimStart('\\', '/'));
                try { rest = Path.GetFullPath(rest); } catch { }
                return "\"" + rest + "\"";
            }

            // Already expanded %BIN%/%LISTS% paths may be unquoted absolute paths
            if (wasQuoted || LooksLikePath(arg))
            {
                if (!Path.IsPathRooted(arg) && (arg.Contains("\\") || arg.Contains("/")))
                {
                    arg = Path.Combine(zapretRoot, arg.TrimStart('\\', '/'));
                }

                try
                {
                    if (Path.IsPathRooted(arg) || File.Exists(arg) || Directory.Exists(Path.GetDirectoryName(arg) ?? string.Empty))
                        arg = Path.GetFullPath(arg);
                }
                catch
                {
                }

                if (wasQuoted || LooksLikePath(arg))
                    return "\"" + arg + "\"";
            }

            return arg;
        }

        private static bool LooksLikePath(string arg)
        {
            if (string.IsNullOrEmpty(arg))
                return false;
            if (arg.Length >= 2 && char.IsLetter(arg[0]) && arg[1] == ':')
                return true;
            if (arg.StartsWith("\\\\", StringComparison.Ordinal))
                return true;
            if (arg.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                arg.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) ||
                arg.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        private static List<string> TokenizeCmdLine(string line)
        {
            var result = new List<string>();
            var sb = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    sb.Append(c);
                    continue;
                }

                if (!inQuotes && char.IsWhiteSpace(c))
                {
                    if (sb.Length > 0)
                    {
                        result.Add(sb.ToString());
                        sb.Clear();
                    }
                    continue;
                }

                sb.Append(c);
            }

            if (sb.Length > 0)
                result.Add(sb.ToString());

            return result;
        }

        private static string BuildRemoveScript()
        {
            return string.Join(
                Environment.NewLine,
                "@echo off",
                "chcp 437 >nul",
                "set SRVCNAME=zapret",
                "sc query %SRVCNAME% >nul 2>&1",
                "if not errorlevel 1 (",
                "  net stop %SRVCNAME%",
                "  sc delete %SRVCNAME%",
                ")",
                "tasklist /FI \"IMAGENAME eq winws.exe\" | find /I \"winws.exe\" >nul",
                "if not errorlevel 1 taskkill /IM winws.exe /F >nul",
                "sc query WinDivert >nul 2>&1",
                "if not errorlevel 1 (",
                "  net stop WinDivert",
                "  sc query WinDivert >nul 2>&1",
                "  if not errorlevel 1 sc delete WinDivert",
                ")",
                "net stop WinDivert14 >nul 2>&1",
                "sc delete WinDivert14 >nul 2>&1",
                "exit /b 0");
        }

        private static string BuildInstallScript(string winwsPath, string args, string strategyName)
        {
            // Match service.bat:
            // sc create zapret binPath= "\"%BIN_PATH%winws.exe\" !ARGS!" DisplayName= "zapret" start= auto
            var sb = new StringBuilder();
            sb.AppendLine("@echo off");
            sb.AppendLine("chcp 437 >nul");
            sb.AppendLine("set SRVCNAME=zapret");
            sb.AppendLine("net stop %SRVCNAME% >nul 2>&1");
            sb.AppendLine("sc delete %SRVCNAME% >nul 2>&1");
            sb.AppendLine("tasklist /FI \"IMAGENAME eq winws.exe\" | find /I \"winws.exe\" >nul");
            sb.AppendLine("if not errorlevel 1 taskkill /IM winws.exe /F >nul 2>&1");
            sb.AppendLine("netsh interface tcp show global | findstr /i \"timestamps\" | findstr /i \"enabled\" >nul || netsh interface tcp set global timestamps=enabled >nul 2>&1");
            sb.Append("set \"WINWS=");
            sb.Append(winwsPath);
            sb.AppendLine("\"");
            sb.Append("set \"ARGS=");
            // Escape % for cmd
            sb.Append(args.Replace("%", "%%"));
            sb.AppendLine("\"");
            sb.AppendLine("sc create %SRVCNAME% binPath= \"\\\"%WINWS%\\\" %ARGS%\" DisplayName= \"zapret\" start= auto");
            sb.AppendLine("if errorlevel 1 exit /b 1");
            sb.AppendLine("sc description %SRVCNAME% \"Zapret DPI bypass software\"");
            sb.AppendLine("sc start %SRVCNAME%");
            sb.Append("reg add \"HKLM\\System\\CurrentControlSet\\Services\\zapret\" /v zapret-discord-youtube /t REG_SZ /d \"");
            sb.Append(strategyName.Replace("\"", string.Empty));
            sb.AppendLine("\" /f");
            sb.AppendLine("exit /b 0");
            return sb.ToString();
        }

        private static void RunElevatedCmd(string scriptContent, string workingDirectory, string title)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), "Zapretik");
            Directory.CreateDirectory(tempDir);
            var scriptPath = Path.Combine(tempDir, "zapret_svc_" + Guid.NewGuid().ToString("N") + ".cmd");
            File.WriteAllText(scriptPath, scriptContent, new UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = scriptPath,
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? Environment.CurrentDirectory
                    : workingDirectory,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };

            try
            {
                using (var process = Process.Start(psi))
                {
                    if (process == null)
                        throw new InvalidOperationException("Не удалось запустить: " + title);

                    if (!process.WaitForExit(120000))
                    {
                        try { process.Kill(); } catch { }
                        throw new TimeoutException("Таймаут: " + title);
                    }

                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException(
                            title + " завершилась с кодом " + process.ExitCode +
                            ".\nНужны права администратора (как у service.bat).");
                    }
                }
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223)
                    throw new OperationCanceledException("Операция отменена в окне UAC.", ex);
                throw;
            }
            finally
            {
                try
                {
                    if (File.Exists(scriptPath))
                        File.Delete(scriptPath);
                }
                catch
                {
                }
            }
        }

        internal sealed class GameFilterValues
        {
            public string GameFilter;
            public string GameFilterTcp;
            public string GameFilterUdp;
        }
    }
}
