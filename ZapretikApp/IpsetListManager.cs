using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretikApp
{
    /// <summary>
    /// Read/write zapret lists\ipset-all.txt (CIDR / host lines).
    /// </summary>
    internal static class IpsetListManager
    {
        public const string RelativeFileName = @"lists\ipset-all.txt";

        private static readonly Regex CidrSuffix = new Regex(
            @"^(?<ip>.+)/(?<prefix>\d{1,3})$",
            RegexOptions.Compiled);

        public static string GetFilePath(string zapretRoot)
        {
            if (string.IsNullOrWhiteSpace(zapretRoot))
                return null;
            return Path.Combine(zapretRoot, "lists", "ipset-all.txt");
        }

        public static bool FileExists(string zapretRoot)
        {
            var path = GetFilePath(zapretRoot);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        public static int CountEntries(string zapretRoot)
        {
            var path = GetFilePath(zapretRoot);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return 0;

            var count = 0;
            foreach (var line in File.ReadLines(path))
            {
                if (IsEntryLine(line))
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Normalize user input to a single ipset line. Returns null if invalid.
        /// </summary>
        public static string NormalizeEntry(string raw, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(raw))
            {
                error = "Введите IP или подсеть (CIDR).";
                return null;
            }

            var text = raw.Trim()
                .Replace(" ", string.Empty)
                .Trim(',', ';');

            // Strip accidental quotes
            text = text.Trim('"', '\'');

            string ipPart;
            int? prefix = null;

            var m = CidrSuffix.Match(text);
            if (m.Success)
            {
                ipPart = m.Groups["ip"].Value;
                int p;
                if (!int.TryParse(m.Groups["prefix"].Value, out p))
                {
                    error = "Некорректный префикс CIDR.";
                    return null;
                }
                prefix = p;
            }
            else
            {
                ipPart = text;
            }

            IPAddress address;
            if (!IPAddress.TryParse(ipPart, out address))
            {
                error = "Некорректный IP-адрес.";
                return null;
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                if (prefix.HasValue && (prefix.Value < 0 || prefix.Value > 32))
                {
                    error = "Для IPv4 префикс должен быть 0–32.";
                    return null;
                }
            }
            else if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (prefix.HasValue && (prefix.Value < 0 || prefix.Value > 128))
                {
                    error = "Для IPv6 префикс должен быть 0–128.";
                    return null;
                }
            }
            else
            {
                error = "Поддерживаются только IPv4 и IPv6.";
                return null;
            }

            // Prefer compact form as in the file (no spaces)
            var normalizedIp = address.ToString();
            if (prefix.HasValue)
                return normalizedIp + "/" + prefix.Value;

            // Host without mask — store as-is (zapret accepts bare IPs in some lists)
            return normalizedIp;
        }

        public static bool Contains(string zapretRoot, string normalizedEntry)
        {
            var path = GetFilePath(zapretRoot);
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || string.IsNullOrEmpty(normalizedEntry))
                return false;

            foreach (var line in File.ReadLines(path))
            {
                var t = line.Trim();
                if (t.Length == 0 || t[0] == '#' || t.StartsWith("//", StringComparison.Ordinal))
                    continue;
                if (string.Equals(t, normalizedEntry, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Append entry if missing. Creates lists\ and file if needed.
        /// </summary>
        public static void Add(string zapretRoot, string normalizedEntry)
        {
            var path = GetFilePath(zapretRoot);
            if (string.IsNullOrEmpty(path))
                throw new InvalidOperationException("Папка Zapret не задана.");

            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(path))
            {
                File.WriteAllText(path, normalizedEntry + Environment.NewLine, Encoding.UTF8);
                return;
            }

            if (Contains(zapretRoot, normalizedEntry))
                throw new InvalidOperationException("Такая запись уже есть в ipset-all.txt.");

            // Ensure file ends with newline before append
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
            {
                if (fs.Length > 0)
                {
                    fs.Seek(-1, SeekOrigin.End);
                    var last = fs.ReadByte();
                    if (last != '\n' && last != '\r')
                    {
                        var nl = Encoding.UTF8.GetBytes(Environment.NewLine);
                        fs.Write(nl, 0, nl.Length);
                    }
                }

                var data = Encoding.UTF8.GetBytes(normalizedEntry + Environment.NewLine);
                fs.Seek(0, SeekOrigin.End);
                fs.Write(data, 0, data.Length);
            }
        }

        public static bool Remove(string zapretRoot, string normalizedEntry)
        {
            var path = GetFilePath(zapretRoot);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            var kept = new List<string>();
            var removed = false;
            foreach (var line in File.ReadAllLines(path))
            {
                var t = line.Trim();
                if (!removed &&
                    t.Length > 0 &&
                    t[0] != '#' &&
                    !t.StartsWith("//", StringComparison.Ordinal) &&
                    string.Equals(t, normalizedEntry, StringComparison.OrdinalIgnoreCase))
                {
                    removed = true;
                    continue;
                }
                kept.Add(line);
            }

            if (!removed)
                return false;

            File.WriteAllLines(path, kept, Encoding.UTF8);
            return true;
        }

        /// <summary>Last N non-comment entries (for a small preview list).</summary>
        public static List<string> GetTailEntries(string zapretRoot, int maxCount)
        {
            var path = GetFilePath(zapretRoot);
            var result = new List<string>();
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || maxCount <= 0)
                return result;

            // Efficient-ish for large files: keep a rolling buffer of last entries
            var buffer = new LinkedList<string>();
            foreach (var line in File.ReadLines(path))
            {
                if (!IsEntryLine(line))
                    continue;
                buffer.AddLast(line.Trim());
                if (buffer.Count > maxCount)
                    buffer.RemoveFirst();
            }

            result.AddRange(buffer);
            return result;
        }

        private static bool IsEntryLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;
            var t = line.TrimStart();
            if (t.Length == 0)
                return false;
            if (t[0] == '#' || t.StartsWith("//", StringComparison.Ordinal))
                return false;
            return true;
        }
    }
}
