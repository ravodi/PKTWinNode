namespace PKTWinNode.Models
{
    public class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ReleaseNotes { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
        public string Sha256Hash { get; set; } = string.Empty;

        public bool IsNewerThan(string currentVersion)
        {
            var newVersion = Version.TrimStart('v');
            var current = currentVersion.TrimStart('v');

            var newParts = ParseVersion(newVersion);
            var currentParts = ParseVersion(current);

            if (newParts.Major > currentParts.Major)
                return true;
            if (newParts.Major < currentParts.Major)
                return false;

            if (newParts.Minor > currentParts.Minor)
                return true;
            if (newParts.Minor < currentParts.Minor)
                return false;

            if (newParts.Patch > currentParts.Patch)
                return true;

            return false;
        }

        private static (int Major, int Minor, int Patch) ParseVersion(string version)
        {
            var parts = version.Split('.');
            return (
                parts.Length > 0 && int.TryParse(parts[0], out var major) ? major : 0,
                parts.Length > 1 && int.TryParse(parts[1], out var minor) ? minor : 0,
                parts.Length > 2 && int.TryParse(parts[2], out var patch) ? patch : 0
            );
        }
    }
}
