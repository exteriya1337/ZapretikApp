namespace ZapretikApp
{
    /// <summary>
    /// Application version and update feed URL.
    /// Bump Current when releasing; keep UpdateManifestUrl pointing at your GitHub raw latest.json.
    /// </summary>
    internal static class AppVersion
    {
        /// <summary>Displayed / compared version (Major.Minor.Patch).</summary>
        public const string Current = "1.0.0";

        /// <summary>
        /// Raw URL to latest.json on GitHub (or any HTTPS host).
        /// After you create the repo, replace OWNER/REPO if needed.
        /// </summary>
        public const string UpdateManifestUrl =
            "https://raw.githubusercontent.com/deepc-dev/ZapretikApp/main/update/latest.json";

        public static string Display
        {
            get { return "v" + Current; }
        }
    }
}
