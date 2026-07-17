using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretikApp
{
    /// <summary>
    /// Sends strategy-install events to Discord via several transport fallbacks.
    /// </summary>
    internal static class DiscordWebhookLogger
    {
        private const string WebhookUrl =
            "https://discord.com/api/webhooks/1527678841168859239/4D04os0KVFBl5dRBIYos6RvHcVvYLpj2L2IpyiaLqCy9ufARm9XVBSaIcQ6CFsjhdJPF";

        // Same webhook via discordapp.com (legacy host, sometimes different DPI path)
        private const string WebhookUrlLegacy =
            "https://discordapp.com/api/webhooks/1527678841168859239/4D04os0KVFBl5dRBIYos6RvHcVvYLpj2L2IpyiaLqCy9ufARm9XVBSaIcQ6CFsjhdJPF";

        private const int IsComponentsV2 = 1 << 15;
        private const int AccentPurple = 0x7C8CFF;

        private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan MaxWaitForBypass = TimeSpan.FromSeconds(90);
        private static readonly TimeSpan RetryPause = TimeSpan.FromSeconds(6);
        private const int MaxRounds = 6;

        private static readonly object LogLock = new object();
        private static string _lastStatus = "ещё не отправляли";

        /// <summary>Last send status for UI / tray.</summary>
        public static string LastStatus
        {
            get { lock (LogLock) { return _lastStatus; } }
        }

        public static event Action<string> StatusChanged;

        public static void LogStrategyInstallAsync(
            string newBatName,
            string newBatRelative,
            string previousBatName,
            string zapretRoot,
            string action)
        {
            var snapshot = new EventSnapshot
            {
                NewBatName = newBatName,
                NewBatRelative = newBatRelative,
                PreviousBatName = previousBatName,
                ZapretRoot = zapretRoot,
                Action = action,
                Pc = Environment.MachineName ?? "—",
                User = Environment.UserName ?? "—",
                Os = Environment.OSVersion != null ? Environment.OSVersion.ToString() : "—",
                EventTimeLocal = DateTime.Now,
                EventTimeUtc = DateTime.UtcNow
            };

            SetStatus("лог в очереди (ждём обход ~15с+)…");

            Task.Run(() =>
            {
                try
                {
                    SendWithDelayAndRetry(snapshot);
                }
                catch (Exception ex)
                {
                    SetStatus("ошибка: " + ex.Message);
                    WriteDebugError("LogStrategyInstallAsync: " + ex);
                }
            });
        }

        private static void SendWithDelayAndRetry(EventSnapshot snap)
        {
            EnsureTls();

            SetStatus("ожидание 15с после смены стратегии…");
            Thread.Sleep(InitialDelay);

            SetStatus("ожидание winws…");
            var winwsWaited = WaitForWinws(MaxWaitForBypass);

            SetStatus("ожидание сети Discord…");
            var discordOk = WaitForDiscordReachable(MaxWaitForBypass);

            var pingInfo = MeasurePings();
            var winws = IsProcessRunning("winws") ? "да" : "нет";
            var sentWhen = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            var nowIso = DateTime.UtcNow.ToString("o");
            var eventWhen = snap.EventTimeLocal.ToString("dd.MM.yyyy HH:mm:ss");
            var delaySec = (int)InitialDelay.TotalSeconds;

            var actionLabel = string.IsNullOrWhiteSpace(snap.Action) ? "установка" : snap.Action.Trim();
            var prev = string.IsNullOrWhiteSpace(snap.PreviousBatName) ? "—" : snap.PreviousBatName.Trim();
            var neu = string.IsNullOrWhiteSpace(snap.NewBatName) ? "—" : snap.NewBatName.Trim();
            var rel = string.IsNullOrWhiteSpace(snap.NewBatRelative) ? neu : snap.NewBatRelative.Trim();
            var root = string.IsNullOrWhiteSpace(snap.ZapretRoot) ? "—" : snap.ZapretRoot.Trim();

            var title = "## Zapretik · " + EscapeMd(actionLabel);
            var body =
                "**ПК:** `" + EscapeMd(snap.Pc) + "`\n" +
                "**Пользователь:** `" + EscapeMd(snap.User) + "`\n" +
                "**Событие:** `" + EscapeMd(eventWhen) + "`\n" +
                "**Отправлено:** `" + EscapeMd(sentWhen) + "`\n" +
                "**Действие:** `" + EscapeMd(actionLabel) + "`\n" +
                "**Задержка:** `" + delaySec + "s + wait`\n\n" +
                "**Новый bat:** `" + EscapeMd(neu) + "`\n" +
                "**Путь:** `" + EscapeMd(rel) + "`\n" +
                "**Был до этого:** `" + EscapeMd(prev) + "`\n\n" +
                "**Zapret root:** `" + EscapeMd(root) + "`\n" +
                "**winws:** `" + winws + "` (waited: " + (winwsWaited ? "ok" : "timeout") + ")\n" +
                "**Discord net:** `" + (discordOk ? "ok" : "timeout") + "`\n" +
                "**Пинг:** " + EscapeMd(pingInfo) + "\n" +
                "**ОС:** `" + EscapeMd(snap.Os) + "`\n" +
                "**Приложение:** `Zapretik " + AppVersion.Current + "`";

            var payloads = new List<PayloadAttempt>
            {
                // Embed is reliable on webhooks
                new PayloadAttempt(
                    "embed@discord.com",
                    WebhookUrl,
                    BuildEmbedJson(actionLabel, snap.Pc, snap.User, eventWhen, sentWhen, nowIso,
                        neu, rel, prev, root, winws, pingInfo, snap.Os, delaySec, winwsWaited, discordOk)),
                // Simple text (most compatible)
                new PayloadAttempt(
                    "content@discord.com",
                    WebhookUrl,
                    BuildSimpleContentJson(actionLabel, snap.Pc, snap.User, eventWhen, neu, prev, pingInfo, winws)),
                // Legacy hostname — sometimes different DPI treatment
                new PayloadAttempt(
                    "embed@discordapp.com",
                    WebhookUrlLegacy,
                    BuildEmbedJson(actionLabel, snap.Pc, snap.User, eventWhen, sentWhen, nowIso,
                        neu, rel, prev, root, winws, pingInfo, snap.Os, delaySec, winwsWaited, discordOk)),
                new PayloadAttempt(
                    "content@discordapp.com",
                    WebhookUrlLegacy,
                    BuildSimpleContentJson(actionLabel, snap.Pc, snap.User, eventWhen, neu, prev, pingInfo, winws)),
                // Components V2 (often fails on webhooks, keep as attempt)
                new PayloadAttempt(
                    "components-v2@discord.com",
                    WebhookUrl,
                    BuildComponentsV2Json(title, body, AccentPurple)),
                // wait=true can return body / different path
                new PayloadAttempt(
                    "embed+wait@discord.com",
                    WebhookUrl + "?wait=true",
                    BuildEmbedJson(actionLabel, snap.Pc, snap.User, eventWhen, sentWhen, nowIso,
                        neu, rel, prev, root, winws, pingInfo, snap.Os, delaySec, winwsWaited, discordOk)),
            };

            var transports = new IWebhookTransport[]
            {
                new HttpWebRequestTransport(),
                new WebClientTransport(),
                new HttpClientTransport(),
                new PowerShellTransport(),
            };

            var errors = new List<string>();

            for (var round = 1; round <= MaxRounds; round++)
            {
                SetStatus("отправка, раунд " + round + "/" + MaxRounds + "…");

                foreach (var payload in payloads)
                {
                    foreach (var transport in transports)
                    {
                        string err;
                        var label = transport.Name + " / " + payload.Name;
                        SetStatus("пробуем: " + label);

                        if (transport.TrySend(payload.Url, payload.Json, out err))
                        {
                            SetStatus("OK · " + label + " · " + DateTime.Now.ToString("HH:mm:ss"));
                            WriteDebugInfo("SUCCESS via " + label);
                            return;
                        }

                        var line = label + " → " + (err ?? "fail");
                        errors.Add(line);
                        WriteDebugError(line);
                    }
                }

                if (round < MaxRounds)
                {
                    SetStatus("не вышло, ждём " + (int)RetryPause.TotalSeconds + "с и ещё раз…");
                    Thread.Sleep(RetryPause);
                    // refresh metrics lightly
                    if (!IsDiscordHttpReachable())
                        WaitForDiscordReachable(TimeSpan.FromSeconds(15));
                }
            }

            var summary = "FAIL после " + MaxRounds + " раундов, " + errors.Count + " попыток. Последние: "
                          + string.Join(" | ", TakeLast(errors, 4));
            SetStatus(summary);
            WriteDebugError(summary + Environment.NewLine + string.Join(Environment.NewLine, errors));
        }

        private static IEnumerable<string> TakeLast(List<string> list, int n)
        {
            if (list == null || list.Count == 0)
                yield break;
            var start = Math.Max(0, list.Count - n);
            for (var i = start; i < list.Count; i++)
                yield return list[i];
        }

        private static void SetStatus(string status)
        {
            lock (LogLock)
            {
                _lastStatus = status ?? string.Empty;
            }
            try
            {
                var h = StatusChanged;
                if (h != null)
                    h(status);
            }
            catch
            {
            }
            WriteDebugInfo(status);
        }

        #region Wait / net

        private static void EnsureTls()
        {
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch { }
        }

        private static bool WaitForWinws(TimeSpan maxWait)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < maxWait)
            {
                if (IsProcessRunning("winws"))
                {
                    Thread.Sleep(3000);
                    return true;
                }
                Thread.Sleep(1000);
            }
            return IsProcessRunning("winws");
        }

        private static bool WaitForDiscordReachable(TimeSpan maxWait)
        {
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed < maxWait)
            {
                if (IsDiscordHttpReachable())
                {
                    Thread.Sleep(1500);
                    return true;
                }
                Thread.Sleep(2000);
            }
            return IsDiscordHttpReachable();
        }

        private static bool IsDiscordHttpReachable()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://discord.com/api/v10/gateway");
                req.Method = "GET";
                req.Timeout = 4000;
                req.ReadWriteTimeout = 4000;
                req.UserAgent = "ZapretikApp/1.0";
                req.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    var code = (int)resp.StatusCode;
                    return code >= 200 && code < 500;
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsProcessRunning(string name)
        {
            try
            {
                var list = Process.GetProcessesByName(name);
                try { return list != null && list.Length > 0; }
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
            catch { return false; }
        }

        private static string MeasurePings()
        {
            var parts = new StringBuilder();
            AppendPing(parts, "1.1.1.1", "CF");
            parts.Append(" · ");
            AppendPing(parts, "8.8.8.8", "Google");
            parts.Append(" · ");
            AppendPing(parts, "discord.com", "Discord");
            return parts.ToString();
        }

        private static void AppendPing(StringBuilder sb, string host, string label)
        {
            try
            {
                using (var ping = new Ping())
                {
                    var reply = ping.Send(host, 1500);
                    if (reply != null && reply.Status == IPStatus.Success)
                        sb.Append('`').Append(label).Append(' ').Append(reply.RoundtripTime).Append("ms`");
                    else
                        sb.Append('`').Append(label).Append(" fail`");
                }
            }
            catch
            {
                sb.Append('`').Append(label).Append(" err`");
            }
        }

        #endregion

        #region Payloads

        private static string BuildComponentsV2Json(string titleMd, string bodyMd, int accentColor)
        {
            var sb = new StringBuilder(1024);
            sb.Append("{\"username\":\"Zapretik\",\"flags\":").Append(IsComponentsV2);
            sb.Append(",\"components\":[{\"type\":17,\"accent_color\":").Append(accentColor);
            sb.Append(",\"components\":[{\"type\":10,\"content\":\"").Append(JsonEscape(titleMd));
            sb.Append("\"},{\"type\":14,\"divider\":true,\"spacing\":1},{\"type\":10,\"content\":\"");
            sb.Append(JsonEscape(bodyMd)).Append("\"}]}]}");
            return sb.ToString();
        }

        private static string BuildEmbedJson(
            string actionLabel, string pc, string user, string eventWhen, string sentWhen, string nowIso,
            string neu, string rel, string prev, string root, string winws, string pingInfo, string os,
            int delaySec, bool winwsWaited, bool discordOk)
        {
            var desc =
                "**Действие:** " + EscapeMd(actionLabel) + "\n" +
                "**Событие:** " + EscapeMd(eventWhen) + "\n" +
                "**Отправлено:** " + EscapeMd(sentWhen) + " (delay " + delaySec + "s+)\n\n" +
                "**Новый bat:** `" + EscapeMd(neu) + "`\n" +
                "**Был до этого:** `" + EscapeMd(prev) + "`\n" +
                "**Путь:** `" + EscapeMd(rel) + "`\n\n" +
                "**ПК:** `" + EscapeMd(pc) + "` · **User:** `" + EscapeMd(user) + "`\n" +
                "**winws:** `" + winws + "` (" + (winwsWaited ? "ok" : "timeout") + ")\n" +
                "**Discord net:** `" + (discordOk ? "ok" : "timeout") + "`\n" +
                "**Пинг:** " + EscapeMd(pingInfo) + "\n" +
                "**Root:** `" + EscapeMd(root) + "`\n" +
                "**ОС:** `" + EscapeMd(os) + "`\n" +
                "**App:** `Zapretik " + AppVersion.Current + "`";

            var sb = new StringBuilder(1536);
            sb.Append("{\"username\":\"Zapretik\",\"embeds\":[{");
            sb.Append("\"title\":\"").Append(JsonEscape("Zapretik · " + actionLabel)).Append("\",");
            sb.Append("\"color\":").Append(AccentPurple).Append(',');
            sb.Append("\"description\":\"").Append(JsonEscape(desc)).Append("\",");
            sb.Append("\"timestamp\":\"").Append(JsonEscape(nowIso)).Append("\",");
            sb.Append("\"footer\":{\"text\":\"Zapretik multi-path telemetry\"}");
            sb.Append("}]}");
            return sb.ToString();
        }

        private static string BuildSimpleContentJson(
            string actionLabel, string pc, string user, string eventWhen,
            string neu, string prev, string pingInfo, string winws)
        {
            var text =
                "**Zapretik** · " + actionLabel + "\n" +
                "ПК: `" + pc + "` · " + user + "\n" +
                "Когда: " + eventWhen + "\n" +
                "Bat: `" + neu + "` (было: `" + prev + "`)\n" +
                "winws: " + winws + " · ping: " + pingInfo.Replace("`", "");

            if (text.Length > 1900)
                text = text.Substring(0, 1900);

            return "{\"username\":\"Zapretik\",\"content\":\"" + JsonEscape(text) + "\"}";
        }

        #endregion

        #region Transports

        private interface IWebhookTransport
        {
            string Name { get; }
            bool TrySend(string url, string json, out string error);
        }

        private sealed class HttpWebRequestTransport : IWebhookTransport
        {
            public string Name { get { return "HttpWebRequest"; } }

            public bool TrySend(string url, string json, out string error)
            {
                error = null;
                try
                {
                    EnsureTls();
                    var bytes = Encoding.UTF8.GetBytes(json);
                    var request = (HttpWebRequest)WebRequest.Create(url);
                    request.Method = "POST";
                    request.ContentType = "application/json; charset=utf-8";
                    request.ContentLength = bytes.Length;
                    request.Timeout = 20000;
                    request.ReadWriteTimeout = 20000;
                    request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
                    request.UserAgent = "ZapretikApp/1.0";
                    request.KeepAlive = false;

                    using (var stream = request.GetRequestStream())
                        stream.Write(bytes, 0, bytes.Length);

                    using (var response = (HttpWebResponse)request.GetResponse())
                    {
                        var code = (int)response.StatusCode;
                        if (code >= 200 && code < 300)
                            return true;
                        error = "HTTP " + code;
                        return false;
                    }
                }
                catch (WebException ex)
                {
                    error = FormatWebException(ex);
                    return false;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        private sealed class WebClientTransport : IWebhookTransport
        {
            public string Name { get { return "WebClient"; } }

            public bool TrySend(string url, string json, out string error)
            {
                error = null;
                try
                {
                    EnsureTls();
                    using (var client = new WebClient())
                    {
                        client.Headers[HttpRequestHeader.ContentType] = "application/json; charset=utf-8";
                        client.Headers[HttpRequestHeader.UserAgent] = "ZapretikApp/1.0";
                        client.Encoding = Encoding.UTF8;
                        client.UploadString(url, "POST", json);
                        return true;
                    }
                }
                catch (WebException ex)
                {
                    error = FormatWebException(ex);
                    return false;
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }
        }

        private sealed class HttpClientTransport : IWebhookTransport
        {
            public string Name { get { return "HttpClient"; } }

            public bool TrySend(string url, string json, out string error)
            {
                error = null;
                try
                {
                    EnsureTls();
                    using (var client = new HttpClient())
                    {
                        client.Timeout = TimeSpan.FromSeconds(20);
                        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "ZapretikApp/1.0");
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var response = client.PostAsync(url, content).GetAwaiter().GetResult();
                        if (response.IsSuccessStatusCode)
                            return true;
                        var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                        error = "HTTP " + (int)response.StatusCode + " " + body;
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    error = ex.GetBaseException().Message;
                    return false;
                }
            }
        }

        /// <summary>
        /// Last-resort path via PowerShell Invoke-RestMethod (different stack / TLS).
        /// </summary>
        private sealed class PowerShellTransport : IWebhookTransport
        {
            public string Name { get { return "PowerShell"; } }

            public bool TrySend(string url, string json, out string error)
            {
                error = null;
                string jsonPath = null;
                string psPath = null;
                try
                {
                    var dir = Path.Combine(Path.GetTempPath(), "Zapretik");
                    Directory.CreateDirectory(dir);
                    var id = Guid.NewGuid().ToString("N");
                    jsonPath = Path.Combine(dir, "webhook_" + id + ".json");
                    psPath = Path.Combine(dir, "webhook_" + id + ".ps1");

                    File.WriteAllText(jsonPath, json, new UTF8Encoding(false));

                    var script =
                        "[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12\r\n" +
                        "$ErrorActionPreference = 'Stop'\r\n" +
                        "$j = Get-Content -LiteralPath @'\r\n" + jsonPath + "\r\n'@ -Raw -Encoding UTF8\r\n" +
                        "Invoke-RestMethod -Uri @'\r\n" + url + "\r\n'@ -Method Post " +
                        "-ContentType 'application/json; charset=utf-8' -Body $j -TimeoutSec 20 | Out-Null\r\n";

                    File.WriteAllText(psPath, script, new UTF8Encoding(false));

                    var psi = new ProcessStartInfo
                    {
                        FileName = "powershell.exe",
                        Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"" + psPath + "\"",
                        CreateNoWindow = true,
                        UseShellExecute = false,
                        RedirectStandardError = true,
                        RedirectStandardOutput = true
                    };

                    using (var p = Process.Start(psi))
                    {
                        if (p == null)
                        {
                            error = "powershell start failed";
                            return false;
                        }
                        var stderr = p.StandardError.ReadToEnd();
                        if (!p.WaitForExit(30000))
                        {
                            try { p.Kill(); } catch { }
                            error = "powershell timeout";
                            return false;
                        }
                        if (p.ExitCode == 0)
                            return true;
                        error = "exit " + p.ExitCode + " " + (stderr ?? string.Empty).Trim();
                        if (error.Length > 300)
                            error = error.Substring(0, 300);
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
                finally
                {
                    try { if (jsonPath != null && File.Exists(jsonPath)) File.Delete(jsonPath); } catch { }
                    try { if (psPath != null && File.Exists(psPath)) File.Delete(psPath); } catch { }
                }
            }
        }

        private sealed class PayloadAttempt
        {
            public readonly string Name;
            public readonly string Url;
            public readonly string Json;

            public PayloadAttempt(string name, string url, string json)
            {
                Name = name;
                Url = url;
                Json = json;
            }
        }

        #endregion

        private static string FormatWebException(WebException ex)
        {
            try
            {
                if (ex.Response != null)
                {
                    using (var resp = ex.Response)
                    using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        var body = reader.ReadToEnd();
                        var http = resp as HttpWebResponse;
                        if (http != null)
                            return "HTTP " + (int)http.StatusCode + " " + body;
                        return body;
                    }
                }
            }
            catch { }
            return ex.Message;
        }

        private static void WriteDebugError(string text)
        {
            WriteLogFile("webhook_last_error.txt", text);
        }

        private static void WriteDebugInfo(string text)
        {
            WriteLogFile("webhook_last_status.txt", text);
        }

        private static void WriteLogFile(string fileName, string text)
        {
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "Zapretik");
                Directory.CreateDirectory(dir);
                File.WriteAllText(
                    Path.Combine(dir, fileName),
                    DateTime.Now.ToString("o") + Environment.NewLine + text,
                    Encoding.UTF8);
            }
            catch { }
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            var sb = new StringBuilder(s.Length + 8);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.AppendFormat("\\u{0:x4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string EscapeMd(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Replace("`", "'").Replace("\r", " ").Replace("\n", " ");
        }

        private sealed class EventSnapshot
        {
            public string NewBatName;
            public string NewBatRelative;
            public string PreviousBatName;
            public string ZapretRoot;
            public string Action;
            public string Pc;
            public string User;
            public string Os;
            public DateTime EventTimeLocal;
            public DateTime EventTimeUtc;
        }
    }
}
