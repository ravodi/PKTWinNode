using System.Net.Http;
using System.Windows;
using System.Windows.Input;
using PKTWinNode.Commands;
using PKTWinNode.Constants;
using PKTWinNode.Helpers;
using PKTWinNode.Models;
using PKTWinNode.Services;

namespace PKTWinNode.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IWslService _wslService;
        private readonly Window _window;
        private bool _autoStartServer;
        private bool _minimizeToTray;
        private bool _runOnStartup;
        private bool _autoStopWslOnClose;
        private string _wslUsername;
        private string _wslPassword;
        private bool _showPassword;
        private string _peerId;
        private string _peerIdSuffix;
        private string _cjdnsPort;
        private string _staticIpAddress;
        private string _subnetMask;
        private string _gateway;
        private string _dnsServers;
        private string _staticIpError = string.Empty;
        private string _subnetMaskError = string.Empty;
        private string _gatewayError = string.Empty;
        private string _dnsServersError = string.Empty;
        private string _cjdnsPortError = string.Empty;
        private string _peerIdError = string.Empty;
        private bool _isRestartingService;
        private bool _isDeletingDistribution;
        private bool _isTogglingService;
        private bool _isServiceRunning;
        private bool _isDistributionInstalled;
        private bool _isServerRunning;
        private bool _isApplyingNetworkConfig;
        private bool _enableAutoPackageUpdates;
        private int _packageUpdateIntervalDays;
        private DateTime? _lastPackageUpdateTime;
        private bool _enableAutoWslRestart;
        private int _wslRestartIntervalDays;
        private DateTime? _lastWslRestartTime;
        private readonly IUpdateService _updateService;
        private bool _isCheckingForUpdates;
        private const string TargetDistribution = ApplicationConstants.TargetDistribution;
        private const string PeerIdPrefix = "PUB_PKT_";

        public SettingsViewModel(ISettingsService settingsService, IWslService wslService, IUpdateService updateService, Window window)
        {
            _settingsService = settingsService;
            _wslService = wslService;
            _updateService = updateService;
            _window = window;

            var settings = _settingsService.LoadSettings();
            _autoStartServer = settings.AutoStartServer;
            _minimizeToTray = settings.MinimizeToTray;
            _autoStopWslOnClose = settings.AutoStopWslOnClose;

            _runOnStartup = StartupHelper.IsInStartup();

            if (settings.RunOnStartup != _runOnStartup)
            {
                settings.RunOnStartup = _runOnStartup;
                _settingsService.SaveSettings(settings);
            }

            _wslUsername = settings.WslUsername;
            _wslPassword = settings.WslPassword;

            if (string.IsNullOrEmpty(settings.PeerId) || !settings.PeerId.StartsWith(PeerIdPrefix))
            {
                _peerId = PeerIdPrefix;
                _peerIdSuffix = string.Empty;
            }
            else
            {
                _peerId = settings.PeerId;
                _peerIdSuffix = settings.PeerId.Substring(PeerIdPrefix.Length);
            }

            _cjdnsPort = settings.CjdnsPort;
            _staticIpAddress = settings.StaticIpAddress;
            _subnetMask = settings.SubnetMask;
            _gateway = settings.Gateway;
            _dnsServers = settings.DnsServers;

            _enableAutoPackageUpdates = settings.EnableAutoPackageUpdates;
            _packageUpdateIntervalDays = settings.PackageUpdateIntervalDays;
            _lastPackageUpdateTime = settings.LastPackageUpdateTime;
            _enableAutoWslRestart = settings.EnableAutoWslRestart;
            _wslRestartIntervalDays = settings.WslRestartIntervalDays;
            _lastWslRestartTime = settings.LastWslRestartTime;

            SaveCommand = new RelayCommand(_ => Save());
            CancelCommand = new RelayCommand(_ => Cancel());
            RestartCjdnsServiceCommand = new RelayCommand(async _ => await RestartCjdnsServiceAsync(), _ => !IsRestartingService && IsDistributionInstalled && IsServerRunning && IsServiceRunning);
            DeleteDistributionCommand = new RelayCommand(async _ => await DeleteDistributionAsync(), _ => !IsDeletingDistribution && !IsServerRunning);
            ToggleServiceRunningCommand = new RelayCommand(async _ => await ToggleServiceRunningAsync(), _ => !IsTogglingService && IsDistributionInstalled && IsServerRunning);
            CheckForUpdatesCommand = new RelayCommand(async _ => await CheckForUpdatesAsync(), _ => !IsCheckingForUpdates);

            _ = LoadServiceStatusAsync();
        }

        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand RestartCjdnsServiceCommand { get; }
        public ICommand DeleteDistributionCommand { get; }
        public ICommand ToggleServiceRunningCommand { get; }
        public ICommand CheckForUpdatesCommand { get; }

        public string CopyrightYear
        {
            get
            {
                const int startYear = 2025;
                int currentYear = DateTime.Now.Year;
                return currentYear > startYear ? $"{startYear}-{currentYear}" : $"{startYear}";
            }
        }

        public bool AutoStartServer
        {
            get => _autoStartServer;
            set => SetProperty(ref _autoStartServer, value);
        }

        public bool MinimizeToTray
        {
            get => _minimizeToTray;
            set => SetProperty(ref _minimizeToTray, value);
        }

        public bool RunOnStartup
        {
            get => _runOnStartup;
            set => SetProperty(ref _runOnStartup, value);
        }

        public bool AutoStopWslOnClose
        {
            get => _autoStopWslOnClose;
            set => SetProperty(ref _autoStopWslOnClose, value);
        }

        public string WslUsername
        {
            get => _wslUsername;
            set => SetProperty(ref _wslUsername, value);
        }

        public string WslPassword
        {
            get => _wslPassword;
            set => SetProperty(ref _wslPassword, value);
        }

        public bool ShowPassword
        {
            get => _showPassword;
            set => SetProperty(ref _showPassword, value);
        }

        public string PeerId
        {
            get => _peerId;
            set
            {

                string newValue = value;

                if (string.IsNullOrEmpty(newValue) || !newValue.StartsWith(PeerIdPrefix))
                {
                    newValue = PeerIdPrefix;
                }

                if (SetProperty(ref _peerId, newValue))
                {

                    _peerIdSuffix = newValue.Length > PeerIdPrefix.Length
                        ? newValue.Substring(PeerIdPrefix.Length)
                        : string.Empty;
                    OnPropertyChanged(nameof(PeerIdSuffix));

                    ValidatePeerId();
                }
            }
        }

        public string PeerIdSuffix
        {
            get => _peerIdSuffix;
            set
            {

                string sanitizedValue = value ?? string.Empty;

                if (!string.IsNullOrEmpty(sanitizedValue))
                {
                    sanitizedValue = new string(sanitizedValue.Where(char.IsDigit).ToArray());
                }

                if (SetProperty(ref _peerIdSuffix, sanitizedValue))
                {

                    _peerId = PeerIdPrefix + _peerIdSuffix;
                    OnPropertyChanged(nameof(PeerId));
                    ValidatePeerId();
                }
            }
        }

        public string CjdnsPort
        {
            get => _cjdnsPort;
            set
            {
                if (SetProperty(ref _cjdnsPort, value))
                {
                    ValidateCjdnsPort();
                }
            }
        }

        public string StaticIpAddress
        {
            get => _staticIpAddress;
            set
            {
                if (SetProperty(ref _staticIpAddress, value))
                {
                    ValidateStaticIp();
                }
            }
        }

        public string SubnetMask
        {
            get => _subnetMask;
            set
            {
                if (SetProperty(ref _subnetMask, value))
                {
                    ValidateSubnetMask();
                }
            }
        }

        public string Gateway
        {
            get => _gateway;
            set
            {
                if (SetProperty(ref _gateway, value))
                {
                    ValidateGateway();
                }
            }
        }

        public string DnsServers
        {
            get => _dnsServers;
            set
            {
                if (SetProperty(ref _dnsServers, value))
                {
                    ValidateDnsServers();
                }
            }
        }

        public string StaticIpError
        {
            get => _staticIpError;
            set => SetProperty(ref _staticIpError, value);
        }

        public string SubnetMaskError
        {
            get => _subnetMaskError;
            set => SetProperty(ref _subnetMaskError, value);
        }

        public string GatewayError
        {
            get => _gatewayError;
            set => SetProperty(ref _gatewayError, value);
        }

        public string DnsServersError
        {
            get => _dnsServersError;
            set => SetProperty(ref _dnsServersError, value);
        }

        public string CjdnsPortError
        {
            get => _cjdnsPortError;
            set => SetProperty(ref _cjdnsPortError, value);
        }

        public string PeerIdError
        {
            get => _peerIdError;
            set => SetProperty(ref _peerIdError, value);
        }

        public bool IsRestartingService
        {
            get => _isRestartingService;
            set
            {
                if (SetProperty(ref _isRestartingService, value))
                {
                    (RestartCjdnsServiceCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsDeletingDistribution
        {
            get => _isDeletingDistribution;
            set
            {
                if (SetProperty(ref _isDeletingDistribution, value))
                {
                    (DeleteDistributionCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsTogglingService
        {
            get => _isTogglingService;
            set
            {
                if (SetProperty(ref _isTogglingService, value))
                {
                    (ToggleServiceRunningCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (RestartCjdnsServiceCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsCheckingForUpdates
        {
            get => _isCheckingForUpdates;
            set
            {
                if (SetProperty(ref _isCheckingForUpdates, value))
                {
                    (CheckForUpdatesCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsServiceRunning
        {
            get => _isServiceRunning;
            set
            {
                if (SetProperty(ref _isServiceRunning, value))
                {
                    (RestartCjdnsServiceCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsDistributionInstalled
        {
            get => _isDistributionInstalled;
            set
            {
                if (SetProperty(ref _isDistributionInstalled, value))
                {
                    (ToggleServiceRunningCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (RestartCjdnsServiceCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(IsDeploymentConfigEditable));
                }
            }
        }

        public bool IsDeploymentConfigEditable => !IsDistributionInstalled;

        public bool IsServerRunning
        {
            get => _isServerRunning;
            set
            {
                if (SetProperty(ref _isServerRunning, value))
                {
                    (ToggleServiceRunningCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (RestartCjdnsServiceCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (DeleteDistributionCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsApplyingNetworkConfig
        {
            get => _isApplyingNetworkConfig;
            set => SetProperty(ref _isApplyingNetworkConfig, value);
        }

        public bool EnableAutoPackageUpdates
        {
            get => _enableAutoPackageUpdates;
            set => SetProperty(ref _enableAutoPackageUpdates, value);
        }

        public int PackageUpdateIntervalDays
        {
            get => _packageUpdateIntervalDays;
            set
            {
                if (value < 1)
                    value = 1;
                SetProperty(ref _packageUpdateIntervalDays, value);
            }
        }

        public string LastPackageUpdateText
        {
            get
            {
                if (_lastPackageUpdateTime == null)
                    return "Status: No automatic updates have run yet";

                var daysSince = (DateTime.UtcNow - _lastPackageUpdateTime.Value).Days;
                if (daysSince == 0)
                    return "Status: Last updated today";
                else if (daysSince == 1)
                    return "Status: Last updated yesterday";
                else
                    return $"Status: Last updated {daysSince} days ago";
            }
        }

        public bool EnableAutoWslRestart
        {
            get => _enableAutoWslRestart;
            set => SetProperty(ref _enableAutoWslRestart, value);
        }

        public int WslRestartIntervalDays
        {
            get => _wslRestartIntervalDays;
            set
            {
                if (value < 1)
                    value = 1;
                SetProperty(ref _wslRestartIntervalDays, value);
            }
        }

        public string LastWslRestartText
        {
            get
            {
                if (_lastWslRestartTime == null)
                    return "Status: No automatic restarts have run yet";

                var daysSince = (DateTime.UtcNow - _lastWslRestartTime.Value).Days;
                if (daysSince == 0)
                    return "Status: Last restarted today";
                else if (daysSince == 1)
                    return "Status: Last restarted yesterday";
                else
                    return $"Status: Last restarted {daysSince} days ago";
            }
        }

        private async Task LoadServiceStatusAsync()
        {
            try
            {

                var serverInfo = await _wslService.GetServerStatusAsync(TargetDistribution);
                IsDistributionInstalled = serverInfo.IsInstalled;
                IsServerRunning = serverInfo.Status == WslServerStatus.Running;

                if (serverInfo.Status != WslServerStatus.Running)
                {
                    return;
                }

                var serviceStatus = await _wslService.GetCjdnsServiceStatusAsync(TargetDistribution);
                if (serviceStatus.IsInstalled)
                {
                    _isServiceRunning = serviceStatus.IsActive;
                    OnPropertyChanged(nameof(IsServiceRunning));
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to load service status.\n\nError: {ex.Message}",
                    "Service Status Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                IsDistributionInstalled = false;
                IsServerRunning = false;
            }
        }

        private async Task ToggleServiceRunningAsync()
        {
            if (IsTogglingService)
                return;

            IsTogglingService = true;

            try
            {

                var serverInfo = await _wslService.GetServerStatusAsync(TargetDistribution);
                if (serverInfo.Status != WslServerStatus.Running)
                {
                    System.Windows.MessageBox.Show(
                        "WSL distribution is not running. Please start the server first.",
                        "Cannot Toggle Service",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var serviceStatus = await _wslService.GetCjdnsServiceStatusAsync(TargetDistribution);
                if (!serviceStatus.IsInstalled)
                {
                    System.Windows.MessageBox.Show(
                        "CJDNS service is not installed.",
                        "Service Not Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                bool result;
                string action;

                if (IsServiceRunning)
                {
                    result = await _wslService.StopCjdnsServiceAsync(TargetDistribution);
                    action = "stopped";
                }
                else
                {
                    result = await _wslService.StartCjdnsServiceAsync(TargetDistribution);
                    action = "started";
                }

                if (result)
                {
                    System.Windows.MessageBox.Show(
                        $"CJDNS service {action} successfully.",
                        "Service Updated",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    await Task.Delay(500);
                    await LoadServiceStatusAsync();
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"Failed to {action.TrimEnd('d')} CJDNS service.",
                        "Operation Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error toggling service: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsTogglingService = false;
            }
        }

        private async Task RestartCjdnsServiceAsync()
        {
            IsRestartingService = true;

            try
            {

                var serverInfo = await _wslService.GetServerStatusAsync(TargetDistribution);
                if (serverInfo.Status != WslServerStatus.Running)
                {
                    System.Windows.MessageBox.Show(
                        "WSL distribution is not running. Please start the server first.",
                        "Cannot Restart Service",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var serviceStatus = await _wslService.GetCjdnsServiceStatusAsync(TargetDistribution);
                if (!serviceStatus.IsInstalled)
                {
                    System.Windows.MessageBox.Show(
                        "CJDNS service is not installed. Please deploy the server with a Peer ID first.",
                        "Service Not Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                var result = await _wslService.RestartCjdnsServiceAsync(TargetDistribution);

                if (result)
                {
                    System.Windows.MessageBox.Show(
                        "CJDNS service restarted successfully.",
                        "Service Restarted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        "Failed to restart CJDNS service. Please check the service status.",
                        "Restart Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error restarting CJDNS service: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsRestartingService = false;
            }
        }

        private async void Save()
        {

            ValidateStaticIp();
            ValidateSubnetMask();
            ValidateGateway();
            ValidateDnsServers();
            ValidateCjdnsPort();
            ValidatePeerId();

            if (!string.IsNullOrWhiteSpace(StaticIpError) ||
                !string.IsNullOrWhiteSpace(SubnetMaskError) ||
                !string.IsNullOrWhiteSpace(GatewayError) ||
                !string.IsNullOrWhiteSpace(DnsServersError) ||
                !string.IsNullOrWhiteSpace(CjdnsPortError) ||
                !string.IsNullOrWhiteSpace(PeerIdError))
            {
                System.Windows.MessageBox.Show(
                    "Please fix the validation errors before saving.",
                    "Validation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var existingSettings = _settingsService.LoadSettings();
            bool networkConfigChanged =
                existingSettings.StaticIpAddress != StaticIpAddress ||
                existingSettings.SubnetMask != SubnetMask ||
                existingSettings.Gateway != Gateway ||
                existingSettings.DnsServers != DnsServers;

            var settings = new AppSettings
            {
                AutoStartServer = AutoStartServer,
                MinimizeToTray = MinimizeToTray,
                RunOnStartup = RunOnStartup,
                AutoStopWslOnClose = AutoStopWslOnClose,
                WslUsername = WslUsername,
                WslPassword = WslPassword,
                PeerId = PeerId,
                CjdnsPort = CjdnsPort,
                StaticIpAddress = StaticIpAddress,
                SubnetMask = SubnetMask,
                Gateway = Gateway,
                DnsServers = DnsServers,
                EnableAutoPackageUpdates = EnableAutoPackageUpdates,
                PackageUpdateIntervalDays = PackageUpdateIntervalDays,
                LastPackageUpdateTime = _lastPackageUpdateTime,
                EnableAutoWslRestart = EnableAutoWslRestart,
                WslRestartIntervalDays = WslRestartIntervalDays,
                LastWslRestartTime = _lastWslRestartTime
            };

            _settingsService.SaveSettings(settings);

            try
            {
                if (RunOnStartup)
                {
                    StartupHelper.AddToStartup();
                }
                else
                {
                    StartupHelper.RemoveFromStartup();
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to update Windows startup setting: {ex.Message}",
                    "Startup Setting Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            if (networkConfigChanged && IsDistributionInstalled)
            {

                if (IsServerRunning)
                {
                    System.Windows.MessageBox.Show(
                        "Network configuration has been saved.\n\n" +
                        "The changes will be applied when you next start the server.\n\n" +
                        "Note: Network settings can only be changed when the server is stopped.",
                        "Network Settings Saved",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    IsApplyingNetworkConfig = true;

                    try
                    {

                        var cidr = NetworkValidationHelper.SubnetMaskToCidr(SubnetMask);
                        var staticIpWithCidr = $"{StaticIpAddress}/{cidr}";

                        bool success = await _wslService.ReconfigureNetworkAsync(
                            TargetDistribution,
                            staticIpWithCidr,
                            Gateway,
                            DnsServers);

                        if (!success)
                        {
                            IsApplyingNetworkConfig = false;

                            System.Windows.MessageBox.Show(
                                "Failed to apply network configuration.\n\n" +
                                "The settings have been saved, but the server network was not updated.\n" +
                                "The changes will be applied when you next start the server.",
                                "Configuration Failed",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);

                            _window.DialogResult = true;
                            _window.Close();
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        IsApplyingNetworkConfig = false;

                        System.Windows.MessageBox.Show(
                            $"Error applying network configuration: {ex.Message}\n\n" +
                            "The settings have been saved, but the server network was not updated.\n" +
                            "The changes will be applied when you next start the server.",
                            "Configuration Error",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);

                        _window.DialogResult = true;
                        _window.Close();
                        return;
                    }
                    finally
                    {
                        IsApplyingNetworkConfig = false;
                    }
                }
            }

            _window.DialogResult = true;
            _window.Close();
        }

        private void ValidateStaticIp()
        {
            if (NetworkValidationHelper.IsValidStaticIpAddress(StaticIpAddress, out string error))
            {
                StaticIpError = string.Empty;
            }
            else
            {
                StaticIpError = error;
            }
        }

        private void ValidateSubnetMask()
        {
            if (NetworkValidationHelper.IsValidSubnetMask(SubnetMask, out string error))
            {
                SubnetMaskError = string.Empty;
            }
            else
            {
                SubnetMaskError = error;
            }
        }

        private void ValidateGateway()
        {
            if (NetworkValidationHelper.IsValidGateway(Gateway, out string error))
            {
                GatewayError = string.Empty;
            }
            else
            {
                GatewayError = error;
            }
        }

        private void ValidateDnsServers()
        {
            if (NetworkValidationHelper.IsValidDnsServers(DnsServers, out string error))
            {
                DnsServersError = string.Empty;
            }
            else
            {
                DnsServersError = error;
            }
        }

        private void ValidateCjdnsPort()
        {
            if (string.IsNullOrWhiteSpace(CjdnsPort))
            {
                CjdnsPortError = "CJDNS Port is required.";
            }
            else if (!int.TryParse(CjdnsPort, out int port) || port < 1 || port > 65535)
            {
                CjdnsPortError = "CJDNS Port must be a valid port number (1-65535).";
            }
            else
            {
                CjdnsPortError = string.Empty;
            }
        }

        private void ValidatePeerId()
        {
            if (string.IsNullOrWhiteSpace(PeerId))
            {
                PeerIdError = "Peer ID is required.";
            }
            else if (PeerId.Trim() == PeerIdPrefix.Trim())
            {
                PeerIdError = "Please enter a numeric value after 'PUB_PKT_'.";
            }
            else if (PeerId.Length <= PeerIdPrefix.Length)
            {
                PeerIdError = "Please enter a numeric value after 'PUB_PKT_'.";
            }
            else if (string.IsNullOrWhiteSpace(PeerIdSuffix))
            {
                PeerIdError = "Please enter a numeric value after 'PUB_PKT_'.";
            }
            else
            {
                PeerIdError = string.Empty;
            }
        }

        private void Cancel()
        {
            _window.DialogResult = false;
            _window.Close();
        }

        private async Task DeleteDistributionAsync()
        {
            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete the {TargetDistribution} WSL distribution?\n\n" +
                "This will permanently remove all data and cannot be undone!\n\n" +
                "You will need to deploy the server again after deletion.",
                "Confirm Deletion",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            IsDeletingDistribution = true;

            try
            {

                var serverInfo = await _wslService.GetServerStatusAsync(TargetDistribution);
                if (!serverInfo.IsInstalled)
                {
                    System.Windows.MessageBox.Show(
                        $"The {TargetDistribution} distribution is not installed.",
                        "Distribution Not Found",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var success = await _wslService.DeleteDistributionAsync(TargetDistribution);

                if (success)
                {
                    System.Windows.MessageBox.Show(
                        $"The {TargetDistribution} distribution has been deleted successfully.\n\n" +
                        "You can deploy a new server from the main window.",
                        "Distribution Deleted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    _window.DialogResult = true;
                    _window.Close();
                }
                else
                {
                    System.Windows.MessageBox.Show(
                        $"Failed to delete the {TargetDistribution} distribution.\n\n" +
                        "Please make sure no WSL processes are running and try again.",
                        "Deletion Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error deleting distribution: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsDeletingDistribution = false;
            }
        }

        private async Task CheckForUpdatesAsync()
        {
            IsCheckingForUpdates = true;

            try
            {
                var updateInfo = await _updateService.CheckForUpdatesAsync();

                if (updateInfo == null)
                {
                    System.Windows.MessageBox.Show(
                        $"You are running the latest version ({_updateService.GetCurrentVersion()}).",
                        "No Updates Available",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var result = System.Windows.MessageBox.Show(
                    $"A new version is available!\n\n" +
                    $"Current version: {_updateService.GetCurrentVersion()}\n" +
                    $"New version: {updateInfo.Version}\n" +
                    $"Size: {updateInfo.FileSize / (1024.0 * 1024.0):F1} MB\n\n" +
                    $"Release Notes:\n{updateInfo.ReleaseNotes}\n\n" +
                    $"Would you like to download and install the update?\n\n" +
                    $"Note: The application will close and restart to complete the update.",
                    "Update Available",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information);

                if (result != MessageBoxResult.Yes)
                    return;

                await DownloadAndInstallUpdateAsync(updateInfo);
            }
            catch (HttpRequestException ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to check for updates.\n\n" +
                    $"Network error: {ex.Message}\n\n" +
                    $"Please check your internet connection and try again.",
                    "Update Check Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Error checking for updates: {ex.Message}",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsCheckingForUpdates = false;
            }
        }

        private async Task DownloadAndInstallUpdateAsync(UpdateInfo updateInfo)
        {
            var progressWindow = new Window
            {
                Title = "Downloading Update",
                Width = 400,
                Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = _window,
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
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
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
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                var installResult = System.Windows.MessageBox.Show(
                    "Download complete!\n\n" +
                    "The application will now close and install the update.\n" +
                    "It will restart automatically when the update is complete.\n\n" +
                    "Click Yes to proceed with installation.",
                    "Ready to Install",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (installResult == MessageBoxResult.Yes)
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
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}
