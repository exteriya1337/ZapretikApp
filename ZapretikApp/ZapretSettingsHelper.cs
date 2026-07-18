using System;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text;
using Microsoft.Win32;

namespace ZapretikApp
{
    /// <summary>
    /// Zapret settings that mirror service.bat menu items 3–5
    /// (Check Status, Game Filter, IPSet Filter).
    /// </summary>
    internal static class ZapretSettingsHelper
    {
        private const string ServiceRegistryPath = @"System\CurrentControlSet\Services\zapret";
        private const string StrategyRegistryValue = "zapret-discord-youtube";
        private const string NoneSentinel = "203.0.113.113/32";

        public enum GameFilterMode
        {
            Disabled,
            All,
            Tcp,
            Udp
        }

        public enum IpsetFilterMode
        {
            /// <summary>Full list loaded (normal).</summary>
            Loaded,
            /// <summary>Placeholder only — effectively blocks nothing useful (service.bat "none").</summary>
            None,
            /// <summary>Empty list — any traffic path (service.bat "any").</summary>
            Any
        }

        public sealed class StatusInfo
        {
            public string StrategyName;
            public string ZapretServiceState;
            public string WinDivertServiceState;
            public bool WinwsRunning;
            public bool ServiceInstalled;
            public string Summary;
        }

        public static string GameFilterFlagPath(string zapretRoot)
        {
            return Path.Combine(zapretRoot ?? string.Empty, "utils", "game_filter.enabled");
        }

        public static string IpsetAllPath(string zapretRoot)
        {
            return Path.Combine(zapretRoot ?? string.Empty, "lists", "ipset-all.txt");
        }

        public static string IpsetBackupPath(string zapretRoot)
        {
            return IpsetAllPath(zapretRoot) + ".backup";
        }

        public static GameFilterMode GetGameFilterMode(string zapretRoot)
        {
            var flag = GameFilterFlagPath(zapretRoot);
            if (string.IsNullOrWhiteSpace(zapretRoot) || !File.Exists(flag))
                return GameFilterMode.Disabled;

            try
            {
                var mode = File.ReadLines(flag)
                    .Select(l => l.Trim())
                    .FirstOrDefault(l => !string.IsNullOrEmpty(l));

                if (string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
                    return GameFilterMode.All;
                if (string.Equals(mode, "tcp", StringComparison.OrdinalIgnoreCase))
                    return GameFilterMode.Tcp;
                if (string.Equals(mode, "udp", StringComparison.OrdinalIgnoreCase))
                    return GameFilterMode.Udp;

                // service.bat treats unknown non-empty as UDP
                if (!string.IsNullOrEmpty(mode))
                    return GameFilterMode.Udp;
            }
            catch
            {
            }

            return GameFilterMode.Disabled;
        }

        public static string FormatGameFilterMode(GameFilterMode mode)
        {
            switch (mode)
            {
                case GameFilterMode.All: return "TCP + UDP";
                case GameFilterMode.Tcp: return "только TCP";
                case GameFilterMode.Udp: return "только UDP";
                default: return "выкл";
            }
        }

        public static void SetGameFilterMode(string zapretRoot, GameFilterMode mode)
        {
            if (string.IsNullOrWhiteSpace(zapretRoot) || !Directory.Exists(zapretRoot))
                throw new InvalidOperationException("Папка Zapret не найдена.");

            var utils = Path.Combine(zapretRoot, "utils");
            Directory.CreateDirectory(utils);
            var flag = GameFilterFlagPath(zapretRoot);

            if (mode == GameFilterMode.Disabled)
            {
                if (File.Exists(flag))
                    File.Delete(flag);
                return;
            }

            string text;
            switch (mode)
            {
                case GameFilterMode.All: text = "all"; break;
                case GameFilterMode.Tcp: text = "tcp"; break;
                case GameFilterMode.Udp: text = "udp"; break;
                default: text = "all"; break;
            }

            File.WriteAllText(flag, text + Environment.NewLine, Encoding.ASCII);
        }

        public static IpsetFilterMode GetIpsetFilterMode(string zapretRoot)
        {
            var listFile = IpsetAllPath(zapretRoot);
            if (string.IsNullOrWhiteSpace(listFile) || !File.Exists(listFile))
                return IpsetFilterMode.Any;

            try
            {
                var lines = File.ReadAllLines(listFile)
                    .Select(l => l.Trim())
                    .Where(l => !string.IsNullOrEmpty(l) &&
                                !l.StartsWith(";", StringComparison.Ordinal) &&
                                !l.StartsWith("#", StringComparison.Ordinal) &&
                                !l.StartsWith("rem ", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (lines.Count == 0)
                    return IpsetFilterMode.Any;

                // service.bat "none": typically a single sentinel line 203.0.113.113/32
                // (avoid treating a full list that happens to mention the IP as none)
                if (lines.Count == 1 &&
                    string.Equals(lines[0], NoneSentinel, StringComparison.OrdinalIgnoreCase))
                    return IpsetFilterMode.None;

                return IpsetFilterMode.Loaded;
            }
            catch
            {
                return IpsetFilterMode.Any;
            }
        }

        public static string FormatIpsetFilterMode(IpsetFilterMode mode)
        {
            switch (mode)
            {
                case IpsetFilterMode.Loaded: return "loaded (список активен)";
                case IpsetFilterMode.None: return "none (заглушка)";
                default: return "any (пустой список)";
            }
        }

        /// <summary>
        /// Cycles loaded → none → any → loaded (same order as service.bat option 5).
        /// </summary>
        public static IpsetFilterMode CycleIpsetFilter(string zapretRoot)
        {
            var current = GetIpsetFilterMode(zapretRoot);
            var listFile = IpsetAllPath(zapretRoot);
            var backupFile = IpsetBackupPath(zapretRoot);
            var listsDir = Path.GetDirectoryName(listFile);
            if (!string.IsNullOrEmpty(listsDir))
                Directory.CreateDirectory(listsDir);

            if (current == IpsetFilterMode.Loaded)
            {
                // → none: backup list, write sentinel
                if (File.Exists(listFile))
                {
                    if (File.Exists(backupFile))
                        File.Delete(backupFile);
                    File.Move(listFile, backupFile);
                }
                File.WriteAllText(listFile, NoneSentinel + Environment.NewLine, Encoding.ASCII);
                return IpsetFilterMode.None;
            }

            if (current == IpsetFilterMode.None)
            {
                // → any: empty file
                File.WriteAllText(listFile, string.Empty, Encoding.ASCII);
                return IpsetFilterMode.Any;
            }

            // any → loaded: restore backup
            if (File.Exists(backupFile))
            {
                if (File.Exists(listFile))
                    File.Delete(listFile);
                File.Move(backupFile, listFile);
                return IpsetFilterMode.Loaded;
            }

            throw new InvalidOperationException(
                "Нет backup списка (ipset-all.txt.backup).\n" +
                "Сначала обновите список IPSet в service.bat (п. 7) или восстановите файл вручную.");
        }

        public static StatusInfo GetStatus(string zapretRoot)
        {
            var info = new StatusInfo
            {
                StrategyName = "—",
                ZapretServiceState = QueryServiceState("zapret"),
                WinDivertServiceState = QueryServiceState("WinDivert"),
                WinwsRunning = IsProcessRunning("winws") || IsProcessRunning("winvs"),
                ServiceInstalled = false
            };

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(ServiceRegistryPath))
                {
                    if (key != null)
                    {
                        info.ServiceInstalled = true;
                        var strategy = key.GetValue(StrategyRegistryValue) as string;
                        if (!string.IsNullOrWhiteSpace(strategy))
                            info.StrategyName = strategy.Trim().Trim('"');
                    }
                }
            }
            catch
            {
            }

            var sb = new StringBuilder();
            sb.AppendLine("Стратегия: " + info.StrategyName);
            sb.AppendLine("Служба zapret: " + info.ZapretServiceState);
            sb.AppendLine("Служба WinDivert: " + info.WinDivertServiceState);
            sb.Append("Bypass (winws): " + (info.WinwsRunning ? "RUNNING" : "NOT running"));
            info.Summary = sb.ToString();
            return info;
        }

        private static string QueryServiceState(string serviceName)
        {
            try
            {
                using (var sc = new ServiceController(serviceName))
                {
                    sc.Refresh();
                    return sc.Status.ToString().ToUpperInvariant();
                }
            }
            catch
            {
                return "NOT installed";
            }
        }

        private static bool IsProcessRunning(string processName)
        {
            try
            {
                var list = System.Diagnostics.Process.GetProcessesByName(processName);
                try
                {
                    return list != null && list.Length > 0;
                }
                finally
                {
                    if (list != null)
                    {
                        foreach (var p in list)
                        {
                            try { p.Dispose(); } catch { }
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
