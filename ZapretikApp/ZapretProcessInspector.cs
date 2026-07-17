using System;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZapretikApp
{
    /// <summary>
    /// Resolves which strategy/bat is associated with running Zapret (winws).
    /// Mirrors Flowseal service.bat logic: registry value on service "zapret".
    /// </summary>
    internal static class ZapretProcessInspector
    {
        private const string ZapretServiceName = "zapret";
        private const string StrategyRegistryValue = "zapret-discord-youtube";
        private const string ServiceRegistryPath = @"System\CurrentControlSet\Services\zapret";

        private static readonly string[] EngineNames = { "winws.exe", "winvs.exe", "winws", "winvs" };

        private static readonly Regex BatPathRegex = new Regex(
            @"(?i)(?<path>(?:[a-zA-Z]:\\|\\\\)[^""\r\n]*?\.(?:bat|cmd)|""([^""\r\n]+?\.(?:bat|cmd))"")",
            RegexOptions.Compiled);

        private static readonly Regex BareBatToken = new Regex(
            @"(?i)(?:^|[\s\\/""'])(?<name>[^\s\\/""']+\.(?:bat|cmd))(?=$|[\s""'&|<>])",
            RegexOptions.Compiled);

        private sealed class ProcRow
        {
            public int Pid;
            public int ParentPid;
            public string Name;
            public string CommandLine;
        }

        /// <summary>
        /// Same source as service.bat option 3 / get_strategy_name:
        /// reg query HKLM\...\Services\zapret /v zapret-discord-youtube
        /// </summary>
        public static ActiveScriptInfo TryGetServiceStrategy(IList<BatFileItem> knownBats, string zapretRoot)
        {
            string strategyFile = null;
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(ServiceRegistryPath))
                {
                    if (key == null)
                        return null;

                    strategyFile = key.GetValue(StrategyRegistryValue) as string;
                }
            }
            catch
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(strategyFile))
                return null;

            strategyFile = strategyFile.Trim().Trim('"');
            // service.bat writes %%~nF (without extension); tolerate both
            var nameNoExt = Path.GetFileNameWithoutExtension(strategyFile);
            if (string.IsNullOrEmpty(nameNoExt))
                nameNoExt = strategyFile;
            var fileName = Path.GetFileName(strategyFile);
            if (string.IsNullOrEmpty(fileName))
                fileName = strategyFile;
            if (!fileName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) &&
                !fileName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
                fileName = nameNoExt + ".bat";

            if (knownBats != null)
            {
                foreach (var bat in knownBats)
                {
                    var batNoExt = Path.GetFileNameWithoutExtension(bat.Name);
                    if (string.Equals(bat.Name, fileName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(batNoExt, nameNoExt, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(bat.RelativePath, strategyFile, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(bat.FullPath, strategyFile, StringComparison.OrdinalIgnoreCase))
                    {
                        return new ActiveScriptInfo(bat.Name, bat.FullPath, "service");
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(zapretRoot))
            {
                try
                {
                    var candidate = Path.Combine(zapretRoot, fileName);
                    if (File.Exists(candidate))
                        return new ActiveScriptInfo(Path.GetFileName(candidate), candidate, "service");

                    // search root for "name.bat" when registry has name without extension
                    foreach (var path in Directory.GetFiles(zapretRoot, nameNoExt + ".*"))
                    {
                        var ext = Path.GetExtension(path);
                        if (ext.Equals(".bat", StringComparison.OrdinalIgnoreCase) ||
                            ext.Equals(".cmd", StringComparison.OrdinalIgnoreCase))
                            return new ActiveScriptInfo(Path.GetFileName(path), path, "service");
                    }
                }
                catch
                {
                }
            }

            return new ActiveScriptInfo(fileName, strategyFile, "service");
        }

        public static bool IsZapretServiceInstalled()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(ServiceRegistryPath))
                    return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsZapretServiceRunning()
        {
            try
            {
                using (var sc = new ServiceController(ZapretServiceName))
                {
                    sc.Refresh();
                    return sc.Status == ServiceControllerStatus.Running;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Strong evidence only: bat path in parent process chain of winws.
        /// </summary>
        public static ActiveScriptInfo TryResolveFromProcessTree(
            IList<BatFileItem> knownBats,
            string zapretRoot)
        {
            Dictionary<int, ProcRow> map;
            try
            {
                map = LoadProcessMap();
            }
            catch
            {
                return null;
            }

            var enginePids = new List<int>();
            foreach (var kv in map)
            {
                if (IsEngineName(kv.Value.Name))
                    enginePids.Add(kv.Key);
            }

            if (enginePids.Count == 0)
                return null;

            foreach (var enginePid in enginePids)
            {
                var fromParents = ResolveFromParentChain(map, enginePid, knownBats, zapretRoot);
                if (fromParents != null)
                    return fromParents;
            }

            foreach (var enginePid in enginePids)
            {
                ProcRow row;
                if (!map.TryGetValue(enginePid, out row) || string.IsNullOrWhiteSpace(row.CommandLine))
                    continue;

                var fromCmd = ExtractBestBatFromCommandLine(row.CommandLine, knownBats, zapretRoot, "cmdline");
                if (fromCmd != null)
                    return fromCmd;
            }

            return null;
        }

        private static ActiveScriptInfo ResolveFromParentChain(
            Dictionary<int, ProcRow> map,
            int enginePid,
            IList<BatFileItem> knownBats,
            string zapretRoot)
        {
            var guard = 0;
            var pid = enginePid;
            while (guard++ < 10)
            {
                ProcRow row;
                if (!map.TryGetValue(pid, out row))
                    break;

                if (pid != enginePid && !string.IsNullOrWhiteSpace(row.CommandLine))
                {
                    var info = ExtractBestBatFromCommandLine(row.CommandLine, knownBats, zapretRoot, "parent");
                    if (info != null)
                        return info;
                }

                if (row.ParentPid <= 0 || row.ParentPid == pid)
                    break;
                pid = row.ParentPid;
            }

            return null;
        }

        private static ActiveScriptInfo ExtractBestBatFromCommandLine(
            string commandLine,
            IList<BatFileItem> knownBats,
            string zapretRoot,
            string source)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                return null;

            var candidates = new List<string>();

            foreach (Match match in BatPathRegex.Matches(commandLine))
            {
                var path = match.Groups["path"].Success
                    ? match.Groups["path"].Value
                    : match.Groups[1].Value;
                path = path.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(path))
                    candidates.Add(path);
            }

            foreach (Match match in BareBatToken.Matches(commandLine))
            {
                var name = match.Groups["name"].Value.Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(name))
                    candidates.Add(name);
            }

            if (candidates.Count == 0)
                return null;

            candidates.Sort((a, b) =>
            {
                var ra = Path.IsPathRooted(a) ? 1 : 0;
                var rb = Path.IsPathRooted(b) ? 1 : 0;
                if (ra != rb) return rb.CompareTo(ra);
                return b.Length.CompareTo(a.Length);
            });

            string rootFull = null;
            if (!string.IsNullOrWhiteSpace(zapretRoot))
            {
                try
                {
                    rootFull = Path.GetFullPath(zapretRoot)
                        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    rootFull = zapretRoot;
                }
            }

            foreach (var raw in candidates)
            {
                var resolved = ResolveCandidatePath(raw, zapretRoot);
                if (string.IsNullOrWhiteSpace(resolved))
                    continue;

                if (IsIgnoredHelperScript(Path.GetFileName(resolved)))
                    continue;

                if (knownBats != null)
                {
                    foreach (var bat in knownBats)
                    {
                        if (string.Equals(bat.FullPath, resolved, StringComparison.OrdinalIgnoreCase))
                            return new ActiveScriptInfo(bat.Name, bat.FullPath, source);
                    }
                }

                if (rootFull != null &&
                    resolved.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                    File.Exists(resolved))
                {
                    return new ActiveScriptInfo(Path.GetFileName(resolved), resolved, source);
                }

                if (Path.IsPathRooted(resolved) && File.Exists(resolved))
                    return new ActiveScriptInfo(Path.GetFileName(resolved), resolved, source);
            }

            if (knownBats != null)
            {
                foreach (var raw in candidates)
                {
                    var fileName = Path.GetFileName(raw);
                    if (string.IsNullOrEmpty(fileName) || IsIgnoredHelperScript(fileName))
                        continue;

                    BatFileItem unique = null;
                    var count = 0;
                    foreach (var bat in knownBats)
                    {
                        if (string.Equals(bat.Name, fileName, StringComparison.OrdinalIgnoreCase))
                        {
                            count++;
                            unique = bat;
                        }
                    }

                    if (count == 1 && unique != null)
                        return new ActiveScriptInfo(unique.Name, unique.FullPath, source);
                }
            }

            return null;
        }

        private static string ResolveCandidatePath(string raw, string zapretRoot)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            raw = raw.Trim().Trim('"');
            try
            {
                if (Path.IsPathRooted(raw))
                    return Path.GetFullPath(raw);

                if (!string.IsNullOrWhiteSpace(zapretRoot))
                    return Path.GetFullPath(Path.Combine(zapretRoot, raw));
            }
            catch
            {
                return raw;
            }

            return raw;
        }

        private static bool IsIgnoredHelperScript(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return true;

            // service.bat itself is the manager, not the strategy
            if (fileName.StartsWith("service", StringComparison.OrdinalIgnoreCase))
                return true;

            string[] ignored =
            {
                "install_easy.cmd", "install_easy.bat",
                "uninstall.cmd", "uninstall.bat",
                "blockcheck.cmd", "blockcheck.bat",
                "elevator.cmd", "elevator.bat"
            };

            foreach (var name in ignored)
            {
                if (fileName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsEngineName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            foreach (var engine in EngineNames)
            {
                if (name.Equals(engine, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static Dictionary<int, ProcRow> LoadProcessMap()
        {
            var map = new Dictionary<int, ProcRow>();
            using (var searcher = new ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, Name, CommandLine FROM Win32_Process"))
            using (var results = searcher.Get())
            {
                foreach (ManagementBaseObject obj in results)
                {
                    using (obj)
                    {
                        try
                        {
                            var pid = Convert.ToInt32(obj["ProcessId"]);
                            var parent = obj["ParentProcessId"] != null ? Convert.ToInt32(obj["ParentProcessId"]) : 0;
                            var name = obj["Name"] as string ?? string.Empty;
                            var cmd = obj["CommandLine"] as string ?? string.Empty;
                            map[pid] = new ProcRow
                            {
                                Pid = pid,
                                ParentPid = parent,
                                Name = name,
                                CommandLine = cmd
                            };
                        }
                        catch
                        {
                        }
                    }
                }
            }

            return map;
        }
    }
}
