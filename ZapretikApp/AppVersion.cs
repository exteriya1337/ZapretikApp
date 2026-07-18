namespace ZapretikApp
{
    /// <summary>
    /// Application version and public URLs.
    /// Bump Current when releasing; UpdateChecker discovers latest via GitHub Releases API + latest.json mirrors.
    /// </summary>
    internal static class AppVersion
    {
        /// <summary>Displayed / compared version (Major.Minor.Patch).</summary>
        public const string Current = "1.0.7";

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

        /// <summary>Google Drive folder id with Zapret distribution (for users who do not have it yet).</summary>
        public const string ZapretDriveFolderId = "16a_u33wp4LqphSkUMnuJie8IEb1p_3Aw";

        /// <summary>Public Google Drive folder URL (manual fallback).</summary>
        public const string ZapretDownloadUrl =
            "https://drive.google.com/drive/folders/" + ZapretDriveFolderId;

        public static string Display
        {
            get { return "v" + Current; }
        }
    }
}
