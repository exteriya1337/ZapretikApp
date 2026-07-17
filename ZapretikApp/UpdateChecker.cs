using System;
using System.Collections.Generic;
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
        /// <summary>Extra download URLs if the primary fails.</summary>
        public string[] AlternateUrls;
        /// <summary>Which feed produced this result (for diagnostics).</summary>
        public string Source;
    }

    /// <summary>
    /// Discovers the latest build.
    /// Primary: GitHub Releases API (no CDN lag).
    /// Secondary: latest.json from several mirrors; prefers the highest version.
    /// jsDelivr is last — @main often serves a stale file for hours.
    /// </summary>
    internal static class UpdateChecker
    {
        private const string RepoOwner = "exteriya1337";
        private const string RepoName = "ZapretikApp";
        private const string ExeAssetName = "ZapretikApp.exe";

        /// <summary>
        /// latest.json mirrors. Order is only a try order — result is max(version).
        /// Avoid relying on jsDelivr first (stale @main cache stuck many clients on 1.0.1).
        /// </summary>
        private static readonly string[] ManifestUrls =
        {
            // GitHub Contents API — raw body, not stuck on raw.githubusercontent CDN
            "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/contents/update/latest.json?ref=main",
            // Attach latest.json to each Release for clients that can hit releases/download
            "https://github.com/" + RepoOwner + "/" + RepoName + "/releases/latest/download/latest.json",
            "https://raw.githubusercontent.com/" + RepoOwner + "/" + RepoName + "/main/update/latest.json",
            "https://github.com/" + RepoOwner + "/" + RepoName + "/raw/main/update/latest.json",
            // CDN last (often reachable in RU, but can lag for many hours)
            "https://cdn.jsdelivr.net/gh/" + RepoOwner + "/" + RepoName + "@main/update/latest.json",
            "https://fastly.jsdelivr.net/gh/" + RepoOwner + "/" + RepoName + "@main/update/latest.json",
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

            // 1) GitHub Releases API — authoritative, no jsDelivr lag
            try
            {
                var fromRelease = FetchFromGitHubReleases();
                if (fromRelease != null)
                    Consider(ref best, ref bestVer, fromRelease);
                else
                    errors.AppendLine("GitHub Releases API → пустой ответ");
            }
            catch (Exception ex)
            {
                errors.AppendLine("GitHub Releases API → " + ex.Message);
            }

            // 2) latest.json mirrors (sha256 + notes + fallback if API blocked)
            foreach (var baseUrl in ManifestUrls)
            {
                try
                {
                    var url = WithCacheBuster(baseUrl);
                    var json = DownloadText(url, preferRawGithubApi: baseUrl.IndexOf("api.github.com", StringComparison.OrdinalIgnoreCase) >= 0);
                    var parsed = ParseManifest(json);
                    if (parsed == null ||
                        string.IsNullOrWhiteSpace(parsed.Version) ||
                        string.IsNullOrWhiteSpace(parsed.Url))
                    {
                        errors.AppendLine(ShortUrl(baseUrl) + " → некорректный JSON");
                        continue;
                    }

                    parsed.Source = ShortUrl(baseUrl);
                    Consider(ref best, ref bestVer, parsed);
                }
                catch (Exception ex)
                {
                    errors.AppendLine(ShortUrl(baseUrl) + " → " + ex.Message);
                }
            }

            if (best != null)
            {
                // Enrich: if best has no sha256, try to copy from any same-version candidate via another fetch is hard;
                // already merged in Consider when versions equal.
                info = best;
                error = null;
                return true;
            }

            error = errors.Length > 0
                ? errors.ToString().Trim()
                : "Не удалось загрузить информацию об обновлении";
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

        /// <summary>
        /// Prefer higher version. Same version → prefer entry with sha256 / better notes / release source.
        /// </summary>
        private static void Consider(ref UpdateInfo best, ref Version bestVer, UpdateInfo candidate)
        {
            if (candidate == null || string.IsNullOrWhiteSpace(candidate.Version))
                return;

            Version ver;
            if (!Version.TryParse(Normalize(candidate.Version), out ver))
                return;

            if (best == null || ver > bestVer)
            {
                best = candidate;
                bestVer = ver;
                return;
            }

            if (ver < bestVer)
                return;

            // Same version: merge fields so we keep sha256 + url + notes from any source.
            if (string.IsNullOrWhiteSpace(best.Sha256) && !string.IsNullOrWhiteSpace(candidate.Sha256))
                best.Sha256 = candidate.Sha256;
            if (string.IsNullOrWhiteSpace(best.Notes) && !string.IsNullOrWhiteSpace(candidate.Notes))
                best.Notes = candidate.Notes;
            if (string.IsNullOrWhiteSpace(best.Url) && !string.IsNullOrWhiteSpace(candidate.Url))
                best.Url = candidate.Url;

            // Collect alternate download URLs
            var urls = new List<string>();
            if (!string.IsNullOrWhiteSpace(best.Url))
                urls.Add(best.Url.Trim());
            if (!string.IsNullOrWhiteSpace(candidate.Url) &&
                !urls.Exists(u => string.Equals(u, candidate.Url.Trim(), StringComparison.OrdinalIgnoreCase)))
                urls.Add(candidate.Url.Trim());
            if (best.AlternateUrls != null)
            {
                foreach (var u in best.AlternateUrls)
                {
                    if (!string.IsNullOrWhiteSpace(u) &&
                        !urls.Exists(x => string.Equals(x, u.Trim(), StringComparison.OrdinalIgnoreCase)))
                        urls.Add(u.Trim());
                }
            }
            if (candidate.AlternateUrls != null)
            {
                foreach (var u in candidate.AlternateUrls)
                {
                    if (!string.IsNullOrWhiteSpace(u) &&
                        !urls.Exists(x => string.Equals(x, u.Trim(), StringComparison.OrdinalIgnoreCase)))
                        urls.Add(u.Trim());
                }
            }
            if (urls.Count > 1)
            {
                best.Url = urls[0];
                best.AlternateUrls = urls.GetRange(1, urls.Count - 1).ToArray();
            }
            else if (urls.Count == 1)
            {
                best.Url = urls[0];
            }

            if (string.IsNullOrWhiteSpace(best.Source))
                best.Source = candidate.Source;
            else if (!string.IsNullOrWhiteSpace(candidate.Source) &&
                     best.Source.IndexOf(candidate.Source, StringComparison.OrdinalIgnoreCase) < 0)
                best.Source = best.Source + " + " + candidate.Source;
        }

        private static UpdateInfo FetchFromGitHubReleases()
        {
            var url = "https://api.github.com/repos/" + RepoOwner + "/" + RepoName + "/releases/latest";
            var json = DownloadText(url, preferRawGithubApi: false);

            var tag = ExtractString(json, "tag_name");
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var version = tag.Trim();
            if (version.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                version = version.Substring(1);

            // Find ZapretikApp.exe asset download URL (first match for our asset name)
            var downloadUrl = ExtractAssetDownloadUrl(json, ExeAssetName);
            if (string.IsNullOrWhiteSpace(downloadUrl))
            {
                // Fallback: conventional release asset URL
                downloadUrl = "https://github.com/" + RepoOwner + "/" + RepoName +
                              "/releases/download/" + tag.Trim() + "/" + ExeAssetName;
            }

            var notes = ExtractString(json, "body");
            if (!string.IsNullOrEmpty(notes) && notes.Length > 400)
                notes = notes.Substring(0, 400).Trim() + "…";

            return new UpdateInfo
            {
                Version = version,
                Url = downloadUrl,
                Notes = notes,
                Sha256 = null,
                Source = "GitHub Releases API",
                AlternateUrls = new[]
                {
                    "https://github.com/" + RepoOwner + "/" + RepoName +
                    "/releases/latest/download/" + ExeAssetName
                }
            };
        }

        /// <summary>
        /// Finds browser_download_url near "name":"ZapretikApp.exe" in releases JSON.
        /// </summary>
        private static string ExtractAssetDownloadUrl(string json, string assetName)
        {
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(assetName))
                return null;

            // Match asset objects roughly: "name":"ZapretikApp.exe" ... "browser_download_url":"..."
            // Also handle reverse order of fields.
            var namePat = "\"name\"\\s*:\\s*\"" + Regex.Escape(assetName) + "\"";
            var m = Regex.Match(json, namePat, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!m.Success)
                return null;

            // Search a window around the name match for browser_download_url
            var start = Math.Max(0, m.Index - 400);
            var len = Math.Min(json.Length - start, 1200);
            var window = json.Substring(start, len);

            var urlM = Regex.Match(
                window,
                "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (urlM.Success)
                return UnescapeJson(urlM.Groups[1].Value);

            // Expand window forward if needed
            start = m.Index;
            len = Math.Min(json.Length - start, 2000);
            window = json.Substring(start, len);
            urlM = Regex.Match(
                window,
                "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            return urlM.Success ? UnescapeJson(urlM.Groups[1].Value) : null;
        }

        private static string WithCacheBuster(string baseUrl)
        {
            // jsDelivr ignores unknown query for cache key of gh packages — still harmless.
            // Important for raw.githubusercontent / github.com.
            if (baseUrl.IndexOf("jsdelivr", StringComparison.OrdinalIgnoreCase) >= 0)
                return baseUrl;
            return baseUrl + (baseUrl.Contains("?") ? "&" : "?") + "_=" + DateTime.UtcNow.Ticks;
        }

        private static string ShortUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return url;
            if (url.Length <= 64)
                return url;
            return url.Substring(0, 61) + "…";
        }

        private static string DownloadText(string url, bool preferRawGithubApi)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.Timeout = 12000;
            request.ReadWriteTimeout = 12000;
            request.UserAgent = "ZapretikApp/" + AppVersion.Current + " (+https://github.com/" + RepoOwner + "/" + RepoName + ")";
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.AllowAutoRedirect = true;
            request.KeepAlive = false;
            request.Headers[HttpRequestHeader.CacheControl] = "no-cache";
            request.Headers[HttpRequestHeader.Pragma] = "no-cache";

            if (preferRawGithubApi || url.IndexOf("api.github.com", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Contents API: raw file body. Releases API: normal JSON (Accept still ok).
                if (url.IndexOf("/contents/", StringComparison.OrdinalIgnoreCase) >= 0)
                    request.Accept = "application/vnd.github.raw";
                else
                    request.Accept = "application/vnd.github+json";
            }

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

            if (json.Length > 0 && json[0] == '\uFEFF')
                json = json.Substring(1);

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
