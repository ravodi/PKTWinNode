using PKTWinNode.Constants;

namespace PKTWinNode.Services
{
    public sealed class SchedulerService : ISchedulerService
    {
        private readonly IWslService _wslService;
        private readonly ISettingsService _settingsService;
        private System.Threading.Timer? _schedulerTimer;
        private const string TargetDistribution = ApplicationConstants.TargetDistribution;
        private bool _disposed;

        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

        public SchedulerService(IWslService wslService, ISettingsService settingsService)
        {
            _wslService = wslService;
            _settingsService = settingsService;
        }

        public void Start()
        {
            if (_schedulerTimer != null)
                return;

            _schedulerTimer = new System.Threading.Timer(
                CheckScheduledTasks,
                null,
                TimeSpan.Zero,
                _checkInterval);
        }

        public void Stop()
        {
            _schedulerTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _schedulerTimer?.Dispose();
            _schedulerTimer = null;
        }

        private async void CheckScheduledTasks(object? state)
        {
            try
            {
                var settings = _settingsService.LoadSettings();
                var now = DateTime.UtcNow;
                bool settingsChanged = false;

                if (settings.EnableAutoPackageUpdates)
                {
                    bool shouldUpdate = false;

                    if (settings.LastPackageUpdateTime == null)
                    {
                        shouldUpdate = true;
                    }
                    else
                    {
                        var timeSinceLastUpdate = now - settings.LastPackageUpdateTime.Value;
                        var intervalDays = settings.PackageUpdateIntervalDays;

                        if (timeSinceLastUpdate.TotalDays >= intervalDays)
                        {
                            shouldUpdate = true;
                        }
                    }

                    if (shouldUpdate)
                    {
                        var success = await _wslService.UpdatePackagesAsync(TargetDistribution);

                        if (success)
                        {
                            settings.LastPackageUpdateTime = now;
                            settingsChanged = true;
                        }
                    }
                }

                if (settings.EnableAutoWslRestart)
                {
                    bool shouldRestart = false;

                    if (settings.LastWslRestartTime == null)
                    {
                        shouldRestart = true;
                    }
                    else
                    {
                        var timeSinceLastRestart = now - settings.LastWslRestartTime.Value;
                        var intervalDays = settings.WslRestartIntervalDays;

                        if (timeSinceLastRestart.TotalDays >= intervalDays)
                        {
                            shouldRestart = true;
                        }
                    }

                    if (shouldRestart)
                    {
                        var success = await _wslService.RebootDistributionAsync(TargetDistribution);

                        if (success)
                        {
                            settings.LastWslRestartTime = now;
                            settingsChanged = true;
                        }
                    }
                }

                if (settingsChanged)
                {
                    _settingsService.SaveSettings(settings);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Scheduled task execution failed.\n\nError: {ex.Message}",
                    "Scheduler Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            Stop();
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }
}
