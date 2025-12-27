using PKTWinNode.Models;

namespace PKTWinNode.Services
{
    public class SettingsService : ISettingsService
    {
        public AppSettings LoadSettings()
        {
            return AppSettings.Load();
        }

        public void SaveSettings(AppSettings settings)
        {
            settings.Save();
        }
    }
}
