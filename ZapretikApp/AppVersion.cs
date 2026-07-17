namespace ZapretikApp
{
    /// <summary>
    /// Application version and update feed URL.
    /// Bump Current when releasing; keep UpdateManifestUrl pointing at your GitHub raw latest.json.
    /// </summary>
    internal static class AppVersion
    {
        /// <summary>Displayed / compared version (Major.Minor.Patch).</summary>
        public const string Current = "1.0.3";

        /// <summary>
        /// Primary feed URL (kept for reference). UpdateChecker also tries CDN mirrors —
        /// raw.githubusercontent.com is often blocked in some regions.
        /// </summary>
        public const string UpdateManifestUrl =
            "https://cdn.jsdelivr.net/gh/exteriya1337/ZapretikApp@main/update/latest.json";

        /// <summary>Public GitHub repository page.</summary>
        public const string GitHubRepoUrl = "https://github.com/exteriya1337/ZapretikApp";

        public static string Display
        {
            get { return "v" + Current; }
        }
    }
}
