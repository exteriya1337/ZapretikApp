namespace ZapretikApp
{
    /// <summary>
    /// Best-effort info about which strategy/script is driving winws.
    /// </summary>
    public sealed class ActiveScriptInfo
    {
        public ActiveScriptInfo(string displayName, string detail, string source)
        {
            DisplayName = displayName ?? string.Empty;
            Detail = detail ?? string.Empty;
            Source = source ?? string.Empty;
        }

        /// <summary>Short name for UI (file name or relative path).</summary>
        public string DisplayName { get; }

        /// <summary>Full path or command-line snippet for tooltip.</summary>
        public string Detail { get; }

        /// <summary>How we learned it: launched | parent | cmdline | last | engine.</summary>
        public string Source { get; }
    }
}
