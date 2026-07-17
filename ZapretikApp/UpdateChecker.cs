using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretikApp
{
    internal sealed class UpdateInfo
    {
        public string Version;
        public string Url;
        public string Sha256;
        public string Notes;
    }

    /// <summary>
    /// Fetches and parses latest.json from the update feed.
    /// Tries several mirrors because raw.githubusercontent.com is often blocked in RU.
    /// </summary>
    internal static class UpdateChecker
    {
        /// <summary>
        /// Ordered mirrors. jsDelivr first — usually reachable when raw.githubusercontent is not.
        /// </summary>
        private static readonly string[] ManifestUrls =
        {
            "https://cdn.jsdelivr.net/gh/exteriya1337/ZapretikApp@main/update/latest.json",
            "https://fastly.jsdelivr.net/gh/exteriya1337/ZapretikApp@main/update/latest.json",
            "https://raw.githubusercontent.com/exteriya1337/ZapretikApp/main/update/latest.json",
            "https://github.com/exteriya1337/ZapretikApp/raw/main/update/latest.json",
        };

        public static bool TryGetLatest(out UpdateInfo info, out string error)
        {
            info = null;
            error = null;

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            }
            catch
            {
            }

            var errors = new StringBuilder();
            UpdateInfo best = null;
            Version bestVer = null;

            foreach (var baseUrl in ManifestUrls)
            {
                try
                {
                    // Cache-buster on raw/github; jsDelivr ignores unknown query but still OK.
                    var url = baseUrl;
                    if (baseUrl.IndexOf("jsdelivr", StringComparison.OrdinalIgnoreCase) < 0)
                        url = baseUrl + (baseUrl.Contains("?") ? "&" : "?") + "_=" + DateTime.UtcNow.Ticks;

                    var json = DownloadText(url);
                    var parsed = ParseManifest(json);
                    if (parsed == null ||
                        string.IsNullOrWhiteSpace(parsed.Version) ||
                        string.IsNullOrWhiteSpace(parsed.Url))
                    {
                        errors.AppendLine(baseUrl + " → некорректный JSON");
                        continue;
                    }

                    Version ver;
                    if (!Version.TryParse(Normalize(parsed.Version), out ver))
                    {
                        errors.AppendLine(baseUrl + " → bad version " + parsed.Version);
                        continue;
                    }

                    // Prefer the highest version across mirrors (CDN can lag behind raw).
                    if (best == null || ver > bestVer)
                    {
                        best = parsed;
                        bestVer = ver;
                    }
                }
                catch (Exception ex)
                {
                    errors.AppendLine(baseUrl + " → " + ex.Message);
                }
            }

            if (best != null)
            {
                info = best;
                error = null;
                return true;
            }

            error = errors.Length > 0
                ? errors.ToString().Trim()
                : "Не удалось загрузить latest.json";
            info = null;
            return false;
        }

        public static bool IsNewerThanCurrent(string remoteVersion)
        {
            Version local;
            Version remote;
            if (!Version.TryParse(Normalize(AppVersion.Current), out local))
                local = new Version(0, 0, 0, 0);
            if (!Version.TryParse(Normalize(remoteVersion), out remote))
                return false;
            return remote > local;
        }

        private static string DownloadText(string url)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            request.UserAgent = "ZapretikApp/" + AppVersion.Current;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.AllowAutoRedirect = true;
            request.KeepAlive = false;
            // Reduce stale proxy/CDN caches where possible
            request.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            request.Headers[HttpRequestHeader.Pragma] = "no-cache";

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var stream = response.GetResponseStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static string Normalize(string v)
        {
            if (string.IsNullOrWhiteSpace(v))
                return "0.0.0";
            v = v.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                v = v.Substring(1);
            // Ensure 4-part-ish parse: 1.0.0 -> 1.0.0.0
            var parts = v.Split('.');
            if (parts.Length == 2)
                v = v + ".0";
            if (parts.Length == 1)
                v = v + ".0.0";
            return v;
        }

        private static UpdateInfo ParseManifest(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            // Strip UTF-8 BOM if present
            if (json.Length > 0 && json[0] == '\uFEFF')
                json = json.Substring(1);

            // Minimal JSON field extraction (no external JSON lib)
            return new UpdateInfo
            {
                Version = ExtractString(json, "version"),
                Url = ExtractString(json, "url"),
                Sha256 = ExtractString(json, "sha256"),
                Notes = ExtractString(json, "notes")
            };
        }

        private static string ExtractString(string json, string key)
        {
            // "key"\s*:\s*"value"
            var pattern = "\"" + Regex.Escape(key) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"";
            var m = Regex.Match(json, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success)
                return null;
            return UnescapeJson(m.Groups[1].Value);
        }

        private static string UnescapeJson(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s;
            return s
                .Replace("\\n", "\n")
                .Replace("\\r", "\r")
                .Replace("\\t", "\t")
                .Replace("\\\"", "\"")
                .Replace("\\\\", "\\");
        }
    }
}
