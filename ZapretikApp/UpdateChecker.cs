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
    /// </summary>
    internal static class UpdateChecker
    {
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

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(AppVersion.UpdateManifestUrl);
                request.Method = "GET";
                request.Timeout = 12000;
                request.ReadWriteTimeout = 12000;
                request.UserAgent = "ZapretikApp/" + AppVersion.Current;
                request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;

                string json;
                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    json = reader.ReadToEnd();
                }

                info = ParseManifest(json);
                if (info == null || string.IsNullOrWhiteSpace(info.Version) || string.IsNullOrWhiteSpace(info.Url))
                {
                    error = "Некорректный latest.json";
                    info = null;
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
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
