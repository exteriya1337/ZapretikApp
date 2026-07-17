namespace ZapretikApp
{
    /// <summary>
    /// Bat/cmd found under the selected Zapret root.
    /// </summary>
    public sealed class BatFileItem
    {
        public BatFileItem(string fullPath, string relativePath)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
        }

        public string FullPath { get; }
        public string RelativePath { get; }
        public string Name
        {
            get { return System.IO.Path.GetFileName(FullPath); }
        }

        public override string ToString()
        {
            return RelativePath;
        }
    }
}
