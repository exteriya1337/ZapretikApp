using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretikApp
{
    /// <summary>
    /// Downloads the public Zapret Google Drive folder (recursive) into a local directory.
    /// </summary>
    internal static class ZapretDriveDownloader
    {
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        private static readonly Regex[] ItemPatterns =
        {
            new Regex(@"data-id=""([a-zA-Z0-9_-]+)""[^>]*data-tooltip=""([^""]+)""", RegexOptions.Compiled),
            new Regex(@"data-tooltip=""([^""]+)""[^>]*data-id=""([a-zA-Z0-9_-]+)""", RegexOptions.Compiled),
            new Regex(@"data-id=""([a-zA-Z0-9_-]+)""[^>]*aria-label=""([^""]+)""", RegexOptions.Compiled),
            new Regex(@"aria-label=""([^""]+)""[^>]*data-id=""([a-zA-Z0-9_-]+)""", RegexOptions.Compiled),
        };

        private static readonly bool[] IdFirst = { true, false, true, false };

        public sealed class Result
        {
            public string Path;
            public int Ok;
            public int Fail;
        }

        public sealed class ProgressInfo
        {
            public string Message;
            /// <summary>0–100. While listing, stays near 0–5.</summary>
            public double Percent;
            public int Done;
            public int Total;
            public bool IsIndeterminate;
        }

        public static string GetDefaultDestination()
        {
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
                desktop = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(desktop, "zapret-discord-youtube");
        }

        /// <summary>
        /// Downloads the whole Drive folder into <paramref name="destRoot"/>.
        /// Optional <paramref name="progress"/> receives progress snapshots (any thread).
        /// </summary>
        public static Result Download(string destRoot, Action<ProgressInfo> progress)
        {
            if (string.IsNullOrWhiteSpace(destRoot))
                throw new ArgumentException("Путь назначения не задан.", "destRoot");

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }

            Directory.CreateDirectory(destRoot);

            Report(progress, "Получение списка файлов…", 0, 0, 0, indeterminate: true);

            var jobs = new List<DownloadJob>();
            CollectJobs(AppVersion.ZapretDriveFolderId, destRoot, 0, jobs, progress);

            if (jobs.Count == 0)
            {
                throw new InvalidOperationException(
                    "Не найдено файлов на Google Drive.\n" +
                    "Откройте вручную:\n" + AppVersion.ZapretDownloadUrl);
            }

            var ok = 0;
            var fail = 0;
            var total = jobs.Count;

            for (var i = 0; i < jobs.Count; i++)
            {
                var job = jobs[i];
                var doneBefore = i;
                var percent = total > 0 ? (100.0 * doneBefore) / total : 0;
                Report(
                    progress,
                    "Скачивание: " + job.DisplayName + "  (" + (i + 1) + "/" + total + ")",
                    percent,
                    doneBefore,
                    total,
                    indeterminate: false);

                if (DownloadFile(job.FileId, job.DestPath))
                    ok++;
                else
                    fail++;

                var done = i + 1;
                percent = total > 0 ? (100.0 * done) / total : 100;
                Report(
                    progress,
                    "Скачано: " + job.DisplayName + "  (" + done + "/" + total + ")",
                    percent,
                    done,
                    total,
                    indeterminate: false);
            }

            if (ok == 0)
            {
                throw new InvalidOperationException(
                    "Не удалось скачать ни одного файла с Google Drive.\n" +
                    "Откройте вручную:\n" + AppVersion.ZapretDownloadUrl);
            }

            var winws = Path.Combine(destRoot, "bin", "winws.exe");
            if (!File.Exists(winws))
            {
                throw new InvalidOperationException(
                    "Скачивание завершилось, но bin\\winws.exe не найден.\n" +
                    "Проверьте папку:\n" + destRoot);
            }

            Report(progress, "Готово: " + ok + " файл(ов)", 100, ok + fail, total, indeterminate: false);
            return new Result
            {
                Path = destRoot,
                Ok = ok,
                Fail = fail
            };
        }

        private static void CollectJobs(
            string folderId,
            string destDir,
            int depth,
            List<DownloadJob> jobs,
            Action<ProgressInfo> progress)
        {
            if (depth > 8)
                return;

            Directory.CreateDirectory(destDir);
            Report(
                progress,
                "Список: " + Path.GetFileName(destDir.TrimEnd('\\', '/')),
                2,
                0,
                0,
                indeterminate: true);

            var items = ListFolder(folderId);
            foreach (var item in items)
            {
                var safeName = SanitizeFileName(item.Name);
                var target = Path.Combine(destDir, safeName);

                if (item.IsFolder)
                {
                    CollectJobs(item.Id, target, depth + 1, jobs, progress);
                    continue;
                }

                jobs.Add(new DownloadJob
                {
                    FileId = item.Id,
                    DestPath = target,
                    DisplayName = safeName
                });

                Report(
                    progress,
                    "В очереди: " + jobs.Count + " файл(ов)…",
                    3,
                    0,
                    jobs.Count,
                    indeterminate: true);
            }
        }

        private static List<DriveItem> ListFolder(string folderId)
        {
            var url = "https://drive.google.com/drive/folders/" + folderId;
            var html = DownloadText(url);
            var map = new Dictionary<string, DriveItem>(StringComparer.Ordinal);

            for (var i = 0; i < ItemPatterns.Length; i++)
            {
                foreach (Match m in ItemPatterns[i].Matches(html))
                {
                    string id, raw;
                    if (IdFirst[i])
                    {
                        id = m.Groups[1].Value;
                        raw = m.Groups[2].Value;
                    }
                    else
                    {
                        raw = m.Groups[1].Value;
                        id = m.Groups[2].Value;
                    }

                    if (string.IsNullOrWhiteSpace(id) || string.Equals(id, folderId, StringComparison.Ordinal))
                        continue;

                    var name = CleanName(raw);
                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (!map.ContainsKey(id))
                    {
                        map[id] = new DriveItem
                        {
                            Id = id,
                            Name = name,
                            IsFolder = IsFolder(raw, name)
                        };
                    }
                }
            }

            var list = new List<DriveItem>(map.Values);
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        private static bool DownloadFile(string fileId, string destPath)
        {
            var urls = new[]
            {
                "https://drive.usercontent.google.com/download?id=" + fileId + "&export=download&confirm=t",
                "https://drive.google.com/uc?export=download&confirm=t&id=" + fileId
            };

            var tmp = destPath + ".partial";
            foreach (var url in urls)
            {
                try
                {
                    if (File.Exists(tmp))
                    {
                        try { File.Delete(tmp); } catch { }
                    }

                    DownloadToFile(url, tmp);
                    if (!File.Exists(tmp))
                        continue;

                    var len = new FileInfo(tmp).Length;
                    if (LooksLikeHtml(tmp, len))
                    {
                        var html = File.ReadAllText(tmp, Encoding.UTF8);
                        var uuidMatch = Regex.Match(html, @"name=""uuid""\s+value=""([^""]+)""");
                        if (!uuidMatch.Success)
                            continue;

                        var retry =
                            "https://drive.usercontent.google.com/download?id=" + fileId +
                            "&export=download&confirm=t&uuid=" + uuidMatch.Groups[1].Value;
                        DownloadToFile(retry, tmp);
                        len = new FileInfo(tmp).Length;
                        if (LooksLikeHtml(tmp, len))
                            continue;
                    }

                    var parent = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(parent))
                        Directory.CreateDirectory(parent);

                    if (File.Exists(destPath))
                        File.Delete(destPath);
                    File.Move(tmp, destPath);
                    return true;
                }
                catch
                {
                    // try next URL
                }
            }

            try
            {
                if (File.Exists(tmp))
                    File.Delete(tmp);
            }
            catch
            {
            }

            return false;
        }

        private static bool LooksLikeHtml(string path, long len)
        {
            if (len < 32)
                return false;
            try
            {
                using (var sr = new StreamReader(path, Encoding.UTF8, true))
                {
                    var buf = new char[64];
                    var n = sr.Read(buf, 0, buf.Length);
                    if (n <= 0)
                        return false;
                    var head = new string(buf, 0, n);
                    return head.IndexOf("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) >= 0 ||
                           head.IndexOf("<html", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string DownloadText(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = UserAgent;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = 120000;
            request.ReadWriteTimeout = 120000;

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream ?? Stream.Null, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static void DownloadToFile(string url, string path)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.UserAgent = UserAgent;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = 300000;
            request.ReadWriteTimeout = 300000;
            request.AllowAutoRedirect = true;

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                if (stream == null)
                    throw new InvalidOperationException("Пустой ответ сервера.");
                stream.CopyTo(fs);
            }
        }

        private static string CleanName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            var n = raw.Trim();
            n = Regex.Replace(n, @"\s+Shared folder\s*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s+folder\s*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s+Unknown\s*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s+Binary\s*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s+Text\s*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s+Document\s*$", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, @"\s+Shared\s*$", "", RegexOptions.IgnoreCase);
            return n.Trim();
        }

        private static bool IsFolder(string raw, string name)
        {
            if (!string.IsNullOrEmpty(raw) &&
                raw.IndexOf("folder", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "lists", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "utils", StringComparison.OrdinalIgnoreCase);
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "file";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Trim();
        }

        private static void Report(
            Action<ProgressInfo> progress,
            string message,
            double percent,
            int done,
            int total,
            bool indeterminate)
        {
            if (progress == null)
                return;
            progress(new ProgressInfo
            {
                Message = message,
                Percent = percent < 0 ? 0 : (percent > 100 ? 100 : percent),
                Done = done,
                Total = total,
                IsIndeterminate = indeterminate
            });
        }

        private sealed class DriveItem
        {
            public string Id;
            public string Name;
            public bool IsFolder;
        }

        private sealed class DownloadJob
        {
            public string FileId;
            public string DestPath;
            public string DisplayName;
        }
    }
}
