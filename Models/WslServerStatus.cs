namespace PKTWinNode.Models
{
    public enum WslServerStatus
    {
        NotInstalled,
        Stopped,
        Running,
        Unknown
    }

    public class WslServerInfo
    {
        public string Name { get; set; } = string.Empty;
        public WslServerStatus Status { get; set; }
        public string Version { get; set; } = string.Empty;
        public bool IsInstalled { get; set; }
        public string Uptime { get; set; } = string.Empty;
    }
}
