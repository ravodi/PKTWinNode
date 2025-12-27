using PKTWinNode.Models;

namespace PKTWinNode.Services
{
    public interface IUpdateService
    {
        Task<UpdateInfo?> CheckForUpdatesAsync();
        Task<string?> DownloadUpdateAsync(UpdateInfo updateInfo, Action<long, long>? progressCallback = null);
        Task<bool> InstallUpdateAsync(string downloadedFilePath);
        string GetCurrentVersion();
    }
}
