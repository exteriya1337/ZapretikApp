namespace ZapretikApp
{
    /// <summary>
    /// Application version and public URLs.
    /// Bump Current when releasing; UpdateChecker discovers latest via GitHub Releases API + latest.json mirrors.
    /// </summary>
    internal static class AppVersion
    {
        /// <summary>Displayed / compared version (Major.Minor.Patch).</summary>
        public const string Current = "1.0.5";

        /// <summary>
        /// Reference feed URL (jsDelivr). Prefer UpdateChecker multi-source discovery at runtime —
        /// raw/CDN alone can lag; Releases API is primary.
        /// </summary>
        public const string UpdateManifestUrl =
            "https://cdn.jsdelivr.net/gh/exteriya1337/ZapretikApp@main/update/latest.json";

        /// <summary>Public GitHub repository page.</summary>
        public const string GitHubRepoUrl = "https://github.com/exteriya1337/ZapretikApp";

        /// <summary>Latest release page.</summary>
        public const string GitHubLatestReleaseUrl =
            "https://github.com/exteriya1337/ZapretikApp/releases/latest";

        public static string Display
        {
            get { return "v" + Current; }
        }
    }
}
