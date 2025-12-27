using System.Text.Json.Serialization;

namespace PKTWinNode.Models
{
    public class PktNodeInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("owner")]
        public string Owner { get; set; } = string.Empty;

        [JsonPropertyName("domain")]
        public string Domain { get; set; } = string.Empty;

        [JsonPropertyName("vpn")]
        public bool Vpn { get; set; }

        [JsonPropertyName("private")]
        public bool Private { get; set; }

        [JsonPropertyName("assigned_yield_credits")]
        public string AssignedYieldCredits { get; set; } = string.Empty;

        [JsonPropertyName("dollarized_yield_credits")]
        public string DollarizedYieldCredits { get; set; } = string.Empty;

        [JsonPropertyName("node_status")]
        public string NodeStatus { get; set; } = string.Empty;

        [JsonPropertyName("peer_id")]
        public string PeerId { get; set; } = string.Empty;

        [JsonPropertyName("operational_status")]
        public OperationalStatus? OperationalStatus { get; set; }
    }

    public class OperationalStatus
    {
        [JsonPropertyName("ipv4")]
        public string Ipv4 { get; set; } = string.Empty;

        [JsonPropertyName("bonus")]
        public Bonus? Bonus { get; set; }

        [JsonPropertyName("uptime")]
        public double Uptime { get; set; }

        [JsonPropertyName("downtime_penalty")]
        public double DowntimePenalty { get; set; }

        [JsonPropertyName("effective_yield_credits")]
        public string EffectiveYieldCredits { get; set; } = string.Empty;

        [JsonPropertyName("daily_yield")]
        public string DailyYield { get; set; } = string.Empty;

        [JsonPropertyName("daily_yield_dollarized")]
        public string DailyYieldDollarized { get; set; } = string.Empty;
    }

    public class Bonus
    {
        [JsonPropertyName("ip_block")]
        public string IpBlock { get; set; } = string.Empty;

        [JsonPropertyName("ip_block_nodes")]
        public int IpBlockNodes { get; set; }

        [JsonPropertyName("ip_block_bonus")]
        public double IpBlockBonus { get; set; }

        [JsonPropertyName("asn")]
        public string Asn { get; set; } = string.Empty;

        [JsonPropertyName("asn_nodes")]
        public int AsnNodes { get; set; }

        [JsonPropertyName("asn_bonus")]
        public double AsnBonus { get; set; }

        [JsonPropertyName("ipv6_bonus")]
        public double Ipv6Bonus { get; set; }

        [JsonPropertyName("total_bonus")]
        public double TotalBonus { get; set; }
    }
}
