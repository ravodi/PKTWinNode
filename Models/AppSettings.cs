using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using PKTWinNode.Constants;

namespace PKTWinNode.Models
{
    public class AppSettings
    {
        public bool AutoStartServer { get; set; }
        public bool MinimizeToTray { get; set; }
        public bool RunOnStartup { get; set; }
        public bool AutoStopWslOnClose { get; set; }
        public string WslUsername { get; set; } = "pktwinnode";
        public string WslPassword { get; set; } = "";
        public string PeerId { get; set; } = "";
        public string CjdnsPort { get; set; } = "55000";
        public string StaticIpAddress { get; set; } = "";
        public string SubnetMask { get; set; } = "";
        public string Gateway { get; set; } = "";
        public string DnsServers { get; set; } = "8.8.8.8, 1.1.1.1";

        public bool EnableAutoPackageUpdates { get; set; } = false;
        public int PackageUpdateIntervalDays { get; set; } = 7;
        public DateTime? LastPackageUpdateTime { get; set; } = null;

        public bool EnableAutoWslRestart { get; set; } = false;
        public int WslRestartIntervalDays { get; set; } = 30;
        public DateTime? LastWslRestartTime { get; set; } = null;

        private static readonly string _settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ApplicationConstants.ApplicationName,
            "settings.json"
        );

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    var json = File.ReadAllText(_settingsPath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();

                    if (string.IsNullOrEmpty(settings.WslPassword))
                    {
                        settings.WslPassword = GenerateRandomPassword();
                        settings.Save();
                    }

                    return settings;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to load application settings.\n\nError: {ex.Message}\n\nDefault settings will be used.",
                    "Settings Load Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }

            var newSettings = new AppSettings();

            if (string.IsNullOrEmpty(newSettings.WslPassword))
            {
                newSettings.WslPassword = GenerateRandomPassword();
                newSettings.Save();
            }

            return newSettings;
        }

        private static string GenerateRandomPassword()
        {
            const string upperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            const string lowerChars = "abcdefghijklmnopqrstuvwxyz";
            const string digitChars = "0123456789";
            const string specialChars = "!@#$%^&*-_=+";

            var password = new char[16];

            password[0] = upperChars[GetSecureRandomNumber(upperChars.Length)];
            password[1] = lowerChars[GetSecureRandomNumber(lowerChars.Length)];
            password[2] = digitChars[GetSecureRandomNumber(digitChars.Length)];
            password[3] = specialChars[GetSecureRandomNumber(specialChars.Length)];

            string allChars = upperChars + lowerChars + digitChars + specialChars;
            for (int i = 4; i < password.Length; i++)
            {
                password[i] = allChars[GetSecureRandomNumber(allChars.Length)];
            }

            for (int i = password.Length - 1; i > 0; i--)
            {
                int j = GetSecureRandomNumber(i + 1);
                (password[i], password[j]) = (password[j], password[i]);
            }

            return new string(password);
        }

        private static int GetSecureRandomNumber(int maxValue)
        {
            if (maxValue <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be greater than 0");

            byte[] randomBytes = new byte[4];
            RandomNumberGenerator.Fill(randomBytes);

            uint randomUInt = BitConverter.ToUInt32(randomBytes, 0);
            return (int)(randomUInt % (uint)maxValue);
        }

        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_settingsPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsPath, json);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to save application settings.\n\nError: {ex.Message}",
                    "Settings Save Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }
    }
}
