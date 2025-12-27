using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Windows;
using PKTWinNode.Models;
using PKTWinNode.Services;
using PKTWinNode.ViewModels;

namespace PKTWinNode.Views
{
    public sealed partial class MainView : Window, IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private bool _disposed;
        private readonly ISettingsService _settingsService;
        private readonly IWslService _wslService;
        private readonly IUpdateService _updateService;
        private AppSettings _settings;
        private bool _isStoppingWsl;

        [RequiresAssemblyFiles()]
        public MainView()
        {
            InitializeComponent();
            _settingsService = new SettingsService();
            _settings = _settingsService.LoadSettings();

            _wslService = new WslService(_settingsService);
            _updateService = new UpdateService();
            var schedulerService = new SchedulerService(_wslService, _settingsService);
            DataContext = new MainViewModel(_wslService, _settingsService, schedulerService, _updateService);

            _notifyIcon = new NotifyIcon
            {
                Icon = System.Drawing.SystemIcons.Application,
                Text = "PKTWinNode",
                Visible = false
            };

            _notifyIcon.DoubleClick += (s, e) => RestoreWindow();

            var contextMenu = new ContextMenuStrip();
            contextMenu.Items.Add("Open", null, (s, e) => RestoreWindow());
            contextMenu.Items.Add("Exit", null, async (s, e) => await CloseApplicationAsync());
            _notifyIcon.ContextMenuStrip = contextMenu;

            StateChanged += OnStateChanged;
            Closing += OnClosing;

            _ = CheckForUpdatesOnStartupAsync();
        }

        private void OnStateChanged(object? sender, EventArgs e)
        {

            _settings = _settingsService.LoadSettings();

            if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
            {
                Hide();
                _notifyIcon.Visible = true;
                _notifyIcon.ShowBalloonTip(2000, "PKTWinNode", "Application minimized to tray", ToolTipIcon.Info);
            }
        }

        private void OnClosing(object? sender, CancelEventArgs e)
        {

            _settings = _settingsService.LoadSettings();

            if (_settings.MinimizeToTray && WindowState != WindowState.Minimized)
            {
                e.Cancel = true;
                WindowState = WindowState.Minimized;
            }
            else if (!_settings.MinimizeToTray)
            {
                if (_settings.AutoStopWslOnClose && !_isStoppingWsl)
                {
                    e.Cancel = true;
                    _ = CloseApplicationAsync();
                }
            }
        }

        private void RestoreWindow()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            _notifyIcon.Visible = false;
        }

        private async Task CloseApplicationAsync()
        {
            if (_isStoppingWsl)
                return;

            _isStoppingWsl = true;
            _notifyIcon.Visible = false;

            _settings = _settingsService.LoadSettings();
            if (_settings.AutoStopWslOnClose)
            {
                try
                {
                    var serverInfo = await _wslService.GetServerStatusAsync("PKTWinNode");
                    if (serverInfo.Status == WslServerStatus.Running)
                    {
                        Title = "PKTWinNode - Stopping WSL...";
                        await _wslService.StopServerAsync("PKTWinNode");
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        $"Failed to stop WSL server during application shutdown.\n\nError: {ex.Message}",
                        "WSL Stop Error",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }

            Dispose();
            System.Windows.Application.Current.Shutdown();
        }

        [RequiresAssemblyFiles("Calls PKTWinNode.Views.MainView.DownloadAndInstallUpdateAsync(UpdateInfo)")]
        private async Task CheckForUpdatesOnStartupAsync()
        {
            try
            {
                await Task.Delay(3000);

                var updateInfo = await _updateService.CheckForUpdatesAsync();
                if (updateInfo != null)
                {
                    var result = System.Windows.MessageBox.Show(
                        $"A new version ({updateInfo.Version}) is available!\n\n" +
                        $"Current version: {_updateService.GetCurrentVersion()}\n\n" +
                        $"Release Notes:\n{updateInfo.ReleaseNotes}\n\n" +
                        $"Would you like to download and install the update?",
                        "Update Available",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Information);

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        await DownloadAndInstallUpdateAsync(updateInfo);
                    }
                }
            }
            catch
            {
            }
        }

        [RequiresAssemblyFiles("Calls PKTWinNode.Services.IUpdateService.InstallUpdateAsync(String)")]
        private async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo)
        {
            var progressWindow = new Window
            {
                Title = "Downloading Update",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize
            };

            var progressBar = new System.Windows.Controls.ProgressBar { Height = 30, Margin = new Thickness(20) };
            var statusText = new System.Windows.Controls.TextBlock
            {
                TextAlignment = System.Windows.TextAlignment.Center,
                Margin = new Thickness(20)
            };

            var stackPanel = new System.Windows.Controls.StackPanel();
            stackPanel.Children.Add(statusText);
            stackPanel.Children.Add(progressBar);
            progressWindow.Content = stackPanel;

            progressWindow.Show();

            try
            {
                string? downloadedPath = null;

                await Task.Run(async () =>
                {
                    downloadedPath = await _updateService.DownloadUpdateAsync(
                        updateInfo,
                        (downloaded, total) =>
                        {
                            Dispatcher.Invoke(() =>
                            {
                                var percent = (int)((double)downloaded / total * 100);
                                progressBar.Value = percent;
                                statusText.Text = $"Downloading: {downloaded / (1024.0 * 1024.0):F1} MB / {total / (1024.0 * 1024.0):F1} MB";
                            });
                        });
                });

                progressWindow.Close();

                if (string.IsNullOrEmpty(downloadedPath))
                {
                    System.Windows.MessageBox.Show(
                        "Failed to download update.",
                        "Download Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return;
                }

                var installResult = System.Windows.MessageBox.Show(
                    "Download complete!\n\n" +
                    "The application will now close and install the update.\n" +
                    "It will restart automatically when the update is complete.\n\n" +
                    "Click Yes to proceed with installation.",
                    "Ready to Install",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Question);

                if (installResult == System.Windows.MessageBoxResult.Yes)
                {
                    await _updateService.InstallUpdateAsync(downloadedPath);
                }
            }
            catch (Exception ex)
            {
                progressWindow?.Close();
                System.Windows.MessageBox.Show(
                    $"Error during update: {ex.Message}",
                    "Update Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            Dispose();
            base.OnClosed(e);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _notifyIcon?.Dispose();
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}
