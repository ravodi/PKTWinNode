using Microsoft.Win32;
using PKTWinNode.Constants;

namespace PKTWinNode.Helpers
{
    public static class StartupHelper
    {
        private const string AppName = ApplicationConstants.ApplicationName;
        private const string RegistryRunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        public static void AddToStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
                if (key != null)
                {
                    string? exePath = Environment.ProcessPath;

                    if (string.IsNullOrEmpty(exePath))
                    {
                        throw new InvalidOperationException("Unable to determine application executable path");
                    }

                    key.SetValue(AppName, $"\"{exePath}\"");
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to add application to startup: {ex.Message}", ex);
            }
        }

        public static void RemoveFromStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, true);
                if (key != null)
                {

                    if (key.GetValue(AppName) != null)
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to remove application from startup: {ex.Message}", ex);
            }
        }

        public static bool IsInStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryRunKey, false);
                if (key != null)
                {
                    var value = key.GetValue(AppName);
                    return value != null;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to check if application is set to run on startup.\n\nError: {ex.Message}",
                    "Startup Check Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }

            return false;
        }
    }
}
