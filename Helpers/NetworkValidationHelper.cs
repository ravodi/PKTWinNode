using System.Text.RegularExpressions;

namespace PKTWinNode.Helpers
{
    public static class NetworkValidationHelper
    {

        public static bool IsValidStaticIpAddress(string ipAddress, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                errorMessage = "Static IP address is required.";
                return false;
            }

            var ip = ipAddress.Contains('/') ? ipAddress.Split('/')[0].Trim() : ipAddress.Trim();

            if (!IsValidIpAddress(ip))
            {
                errorMessage = "Invalid IP address. Please use format: XXX.XXX.XXX.XXX (e.g., 192.168.1.100).";
                return false;
            }

            return true;
        }

        public static bool IsValidSubnetMask(string subnetMask, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(subnetMask))
            {
                errorMessage = "Subnet mask is required.";
                return false;
            }

            var mask = subnetMask.Trim();

            if (!IsValidIpAddress(mask))
            {
                errorMessage = "Invalid subnet mask format. Please use format: XXX.XXX.XXX.XXX (e.g., 255.255.255.0).";
                return false;
            }

            var parts = mask.Split('.');
            if (parts.Length != 4)
            {
                errorMessage = "Invalid subnet mask format.";
                return false;
            }

            uint maskValue = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!byte.TryParse(parts[i], out byte octet))
                {
                    errorMessage = "Invalid subnet mask format.";
                    return false;
                }
                maskValue = (maskValue << 8) | octet;
            }

            uint inverted = ~maskValue;
            if ((inverted & (inverted + 1)) != 0)
            {
                errorMessage = "Invalid subnet mask. Must be a valid network mask (e.g., 255.255.255.0, 255.255.0.0).";
                return false;
            }

            return true;
        }

        public static int SubnetMaskToCidr(string subnetMask)
        {
            if (string.IsNullOrWhiteSpace(subnetMask))
                return -1;

            var parts = subnetMask.Split('.');
            if (parts.Length != 4)
                return -1;

            int cidr = 0;
            foreach (var part in parts)
            {
                if (!byte.TryParse(part, out byte octet))
                    return -1;

                for (int i = 7; i >= 0; i--)
                {
                    if ((octet & (1 << i)) != 0)
                        cidr++;
                    else
                        break;
                }
            }

            return cidr;
        }

        public static bool IsValidGateway(string gateway, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(gateway))
            {
                errorMessage = "Gateway is required.";
                return false;
            }

            if (!IsValidIpAddress(gateway.Trim()))
            {
                errorMessage = "Invalid gateway IP address. Please use format: XXX.XXX.XXX.XXX (e.g., 192.168.1.1).";
                return false;
            }

            return true;
        }

        public static bool IsValidDnsServers(string dnsServers, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrWhiteSpace(dnsServers))
            {
                errorMessage = "At least one DNS server is required.";
                return false;
            }

            var servers = dnsServers.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            if (servers.Length == 0)
            {
                errorMessage = "At least one DNS server is required.";
                return false;
            }

            foreach (var server in servers)
            {
                if (!IsValidIpAddress(server))
                {
                    errorMessage = $"Invalid DNS server IP address: {server}. Please use format: XXX.XXX.XXX.XXX.";
                    return false;
                }
            }

            return true;
        }

        private static bool IsValidIpAddress(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return false;

            var ipPattern = @"^(\d{1,3})\.(\d{1,3})\.(\d{1,3})\.(\d{1,3})$";
            var match = Regex.Match(ip, ipPattern);

            if (!match.Success)
                return false;

            for (int i = 1; i <= 4; i++)
            {
                if (!int.TryParse(match.Groups[i].Value, out int octet) || octet < 0 || octet > 255)
                {
                    return false;
                }
            }

            return true;
        }

    }
}
