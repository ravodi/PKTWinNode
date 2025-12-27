using PKTWinNode.Models;

namespace PKTWinNode.Services
{
    public interface IWslService
    {
        Task<bool> IsWslInstalledAsync();
        Task<bool> IsHyperVInstalledAsync();
        Task<bool> IsRestartPendingAsync();
        Task<WslServerInfo> GetServerStatusAsync(string distributionName);
        Task<bool> StartServerAsync(string distributionName);
        Task<bool> StopServerAsync(string distributionName);
        Task<bool> InstallWslAsync();
        Task<string[]> GetInstalledDistributionsAsync();
        Task<bool> DeployDistributionAsync(string distributionName, string username = "pktwinnode", string password = "pktwinnode", string peerId = "", string cjdnsPort = "", string staticIp = "", Action<string, int>? progressCallback = null);
        Task<CjdnsServiceStatus> GetCjdnsServiceStatusAsync(string distributionName);
        Task<bool> RestartCjdnsServiceAsync(string distributionName);
        Task<bool> StartCjdnsServiceAsync(string distributionName);
        Task<bool> StopCjdnsServiceAsync(string distributionName);
        Task<bool> DeleteDistributionAsync(string distributionName);
        Task<bool> ReconfigureNetworkAsync(string distributionName, string staticIp, string gateway, string dnsServers);
        Task<bool> CheckInternetConnectivityAsync(string distributionName);
        Task<bool> UpdatePackagesAsync(string distributionName);
        Task<bool> RebootDistributionAsync(string distributionName);
    }
}
