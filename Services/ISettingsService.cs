using PKTWinNode.Models;

namespace PKTWinNode.Services
{
    public interface ISettingsService
    {
        AppSettings LoadSettings();
        void SaveSettings(AppSettings settings);
    }
}
