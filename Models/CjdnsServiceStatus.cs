namespace PKTWinNode.Models
{
    public class CjdnsServiceStatus
    {
        public bool IsInstalled { get; set; }
        public bool IsActive { get; set; }
        public bool IsEnabled { get; set; }
        public string State { get; set; } = string.Empty;
        public string SubState { get; set; } = string.Empty;
        public string MainPID { get; set; } = string.Empty;
        public string Memory { get; set; } = string.Empty;
        public string Uptime { get; set; } = string.Empty;
        public string StatusText { get; set; } = string.Empty;
    }
}
