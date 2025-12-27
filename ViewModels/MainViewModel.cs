using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using PKTWinNode.Commands;
using PKTWinNode.Constants;
using PKTWinNode.Helpers;
using PKTWinNode.Models;
using PKTWinNode.Services;
using PKTWinNode.Views;
using MessageBox = System.Windows.MessageBox;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxImage = System.Windows.MessageBoxImage;

namespace PKTWinNode.ViewModels
{
    public sealed class MainViewModel : ViewModelBase, IDisposable
    {
        private readonly IWslService _wslService;
        private readonly ISettingsService _settingsService;
        private readonly ISchedulerService _schedulerService;
        private readonly DispatcherTimer _statusTimer;
        private readonly DispatcherTimer _nodeInfoTimer;
        private const string TargetDistribution = ApplicationConstants.TargetDistribution;
        private bool _disposed;
        private string _statusMessage = "Initializing...";
        private WslServerStatus _currentStatus = WslServerStatus.Unknown;
        private bool _isWslInstalled;
        private bool _isHyperVInstalled;
        private bool _isRestartPending;
        private bool _isLoading = true;
        private bool _isInstallingWsl;
        private string _wslInstallationStatus = string.Empty;
        private bool _wslInstallationComplete;
        private bool _showWslInstallConfirmation;
        private bool _showRestartConfirmation;
        private bool _showDeployConfirmation;
        private string _distributionVersion = string.Empty;
        private bool _isDistributionInstalled;
        private AppSettings _appSettings;
        private bool _isPeerIdConfigured;
        private CjdnsServiceStatus? _cjdnsServiceStatus;
        private string _wslUptime = string.Empty;
        private PktNodeInfo? _pktNodeInfo;
        private bool _isLoadingNodeInfo;
        private string _nodeInfoError = string.Empty;
        private readonly HttpClient _httpClient;
        private bool _isDeploying;
        private int _deploymentProgress;
        private string _deploymentStatus = string.Empty;
        private bool _deploymentComplete;
        private bool _deploymentFailed;
        private bool _showServerControls;
        private bool _hasInternetConnection;
        private readonly IUpdateService _updateService;

        public MainViewModel(IWslService wslService, ISettingsService settingsService, ISchedulerService schedulerService, IUpdateService updateService)
        {
            _wslService = wslService;
            _settingsService = settingsService;
            _schedulerService = schedulerService;
            _updateService = updateService;
            _appSettings = _settingsService.LoadSettings();
            _isPeerIdConfigured = !string.IsNullOrWhiteSpace(_appSettings.PeerId);
            _httpClient = new HttpClient();

            StartServerCommand = new RelayCommand(async _ => await StartServerAsync(), _ => CanStartServer());
            StopServerCommand = new RelayCommand(async _ => await StopServerAsync(), _ => CanStopServer());
            InstallWslCommand = new RelayCommand(async _ => await InstallWslAsync(), _ => (!IsWslInstalled || !IsHyperVInstalled) && !IsLoading && !IsInstallingWsl);
            DeployServerCommand = new RelayCommand(async _ => await DeployServerAsync(), _ => CanDeployServer());
            OpenSettingsCommand = new RelayCommand(_ => OpenSettings(), _ => !IsDeploying);
            RestartSystemCommand = new RelayCommand(_ => ShowRestartConfirmationDialog(), _ => WslInstallationComplete && !ShowRestartConfirmation);
            ConfirmInstallWslCommand = new RelayCommand(async _ => await ConfirmInstallWslAsync());
            CancelInstallWslCommand = new RelayCommand(_ => CancelInstallWsl());
            ConfirmRestartCommand = new RelayCommand(_ => ConfirmRestart());
            CancelRestartCommand = new RelayCommand(_ => CancelRestart());
            ConfirmDeployCommand = new RelayCommand(async _ => await ConfirmDeployAsync());
            CancelDeployCommand = new RelayCommand(_ => CancelDeploy());
            ContinueAfterDeploymentCommand = new RelayCommand(async _ => await ContinueAfterDeploymentAsync());

            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _statusTimer.Tick += async (s, e) => await RefreshStatusAsync(silent: true);

            _nodeInfoTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(30)
            };
            _nodeInfoTimer.Tick += async (s, e) => await FetchNodeInfoAsync(silent: true);

            _ = InitializeAsync();
        }

        public ICommand StartServerCommand { get; }
        public ICommand StopServerCommand { get; }
        public ICommand InstallWslCommand { get; }
        public ICommand DeployServerCommand { get; }
        public ICommand OpenSettingsCommand { get; }
        public ICommand RestartSystemCommand { get; }
        public ICommand ConfirmInstallWslCommand { get; }
        public ICommand CancelInstallWslCommand { get; }
        public ICommand ConfirmRestartCommand { get; }
        public ICommand CancelRestartCommand { get; }
        public ICommand ConfirmDeployCommand { get; }
        public ICommand CancelDeployCommand { get; }
        public ICommand ContinueAfterDeploymentCommand { get; }

        public bool IsDistributionInstalled
        {
            get => _isDistributionInstalled;
            set
            {
                if (SetProperty(ref _isDistributionInstalled, value))
                {
                    OnPropertyChanged(nameof(IsServerDeployed));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public bool IsServerDeployed => IsDistributionInstalled;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public WslServerStatus CurrentStatus
        {
            get => _currentStatus;
            set
            {
                if (SetProperty(ref _currentStatus, value))
                {
                    UpdateStatusMessage();
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public bool IsWslInstalled
        {
            get => _isWslInstalled;
            set
            {
                if (SetProperty(ref _isWslInstalled, value))
                {
                    OnPropertyChanged(nameof(NeedsWslInstall));
                    OnPropertyChanged(nameof(ShowDependenciesSection));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public bool IsHyperVInstalled
        {
            get => _isHyperVInstalled;
            set
            {
                if (SetProperty(ref _isHyperVInstalled, value))
                {
                    OnPropertyChanged(nameof(NeedsHyperVInstall));
                    OnPropertyChanged(nameof(ShowDependenciesSection));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public bool IsRestartPending
        {
            get => _isRestartPending;
            set
            {
                if (SetProperty(ref _isRestartPending, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    OnPropertyChanged(nameof(ShowDependenciesSection));
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public bool IsInstallingWsl
        {
            get => _isInstallingWsl;
            set
            {
                if (SetProperty(ref _isInstallingWsl, value))
                {
                    RaiseCommandsCanExecuteChanged();
                    OnPropertyChanged(nameof(ShowInstallWslButton));
                }
            }
        }

        public string WslInstallationStatus
        {
            get => _wslInstallationStatus;
            set => SetProperty(ref _wslInstallationStatus, value);
        }

        public bool WslInstallationComplete
        {
            get => _wslInstallationComplete;
            set
            {
                if (SetProperty(ref _wslInstallationComplete, value))
                {
                    OnPropertyChanged(nameof(ShowInstallWslButton));
                    OnPropertyChanged(nameof(ShowRestartButton));
                    OnPropertyChanged(nameof(ShowDependenciesSection));
                }
            }
        }

        public bool ShowWslInstallConfirmation
        {
            get => _showWslInstallConfirmation;
            set
            {
                if (SetProperty(ref _showWslInstallConfirmation, value))
                {
                    OnPropertyChanged(nameof(ShowInstallWslButton));
                }
            }
        }

        public bool NeedsHyperVInstall => !IsHyperVInstalled;
        public bool NeedsWslInstall => !IsWslInstalled;

        public bool ShowDependenciesSection => !IsLoading && (!IsWslInstalled || !IsHyperVInstalled || WslInstallationComplete);

        public bool ShowRestartConfirmation
        {
            get => _showRestartConfirmation;
            set
            {
                if (SetProperty(ref _showRestartConfirmation, value))
                {
                    OnPropertyChanged(nameof(ShowRestartButton));
                }
            }
        }

        public bool ShowDeployConfirmation
        {
            get => _showDeployConfirmation;
            set => SetProperty(ref _showDeployConfirmation, value);
        }

        public bool ShowInstallWslButton => !WslInstallationComplete && !ShowWslInstallConfirmation && !IsInstallingWsl;

        public bool ShowRestartButton => WslInstallationComplete && !ShowRestartConfirmation;

        public string DistributionVersion
        {
            get => _distributionVersion;
            set => SetProperty(ref _distributionVersion, value);
        }

        public bool IsPeerIdConfigured
        {
            get => _isPeerIdConfigured;
            set => SetProperty(ref _isPeerIdConfigured, value);
        }

        public bool IsNetworkConfigured =>
            NetworkValidationHelper.IsValidStaticIpAddress(_appSettings.StaticIpAddress, out _) &&
            NetworkValidationHelper.IsValidDnsServers(_appSettings.DnsServers, out _);

        public bool IsCjdnsPortConfigured => !string.IsNullOrWhiteSpace(_appSettings.CjdnsPort);

        public bool IsFullyConfigured => IsPeerIdConfigured && IsNetworkConfigured && IsCjdnsPortConfigured;

        public CjdnsServiceStatus? CjdnsServiceStatus
        {
            get => _cjdnsServiceStatus;
            set => SetProperty(ref _cjdnsServiceStatus, value);
        }

        public string WslUptime
        {
            get => _wslUptime;
            set => SetProperty(ref _wslUptime, value);
        }

        public PktNodeInfo? PktNodeInfo
        {
            get => _pktNodeInfo;
            set
            {
                if (SetProperty(ref _pktNodeInfo, value))
                {
                    OnPropertyChanged(nameof(HasOperationalStatus));
                }
            }
        }

        public bool HasOperationalStatus => _pktNodeInfo?.OperationalStatus != null;

        public bool IsLoadingNodeInfo
        {
            get => _isLoadingNodeInfo;
            set
            {
                if (SetProperty(ref _isLoadingNodeInfo, value))
                {
                    RaiseCommandsCanExecuteChanged();
                }
            }
        }

        public string NodeInfoError
        {
            get => _nodeInfoError;
            set => SetProperty(ref _nodeInfoError, value);
        }

        public bool IsDeploying
        {
            get => _isDeploying;
            set
            {
                if (SetProperty(ref _isDeploying, value))
                {
                    (OpenSettingsCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public int DeploymentProgress
        {
            get => _deploymentProgress;
            set => SetProperty(ref _deploymentProgress, value);
        }

        public string DeploymentStatus
        {
            get => _deploymentStatus;
            set => SetProperty(ref _deploymentStatus, value);
        }

        public bool DeploymentComplete
        {
            get => _deploymentComplete;
            set => SetProperty(ref _deploymentComplete, value);
        }

        public bool DeploymentFailed
        {
            get => _deploymentFailed;
            set => SetProperty(ref _deploymentFailed, value);
        }

        public bool ShowServerControls
        {
            get => _showServerControls;
            set => SetProperty(ref _showServerControls, value);
        }

        public bool HasInternetConnection
        {
            get => _hasInternetConnection;
            set => SetProperty(ref _hasInternetConnection, value);
        }

        private async Task InitializeAsync()
        {
            IsLoading = true;

            try
            {
                IsWslInstalled = await _wslService.IsWslInstalledAsync();
                IsHyperVInstalled = await _wslService.IsHyperVInstalledAsync();
                IsRestartPending = await _wslService.IsRestartPendingAsync();

                OnPropertyChanged(nameof(NeedsHyperVInstall));
                OnPropertyChanged(nameof(NeedsWslInstall));

                if (IsRestartPending)
                {
                    WslInstallationComplete = true;
                    CurrentStatus = WslServerStatus.NotInstalled;
                    IsDistributionInstalled = false;
                    IsLoading = false;
                }
                else if (IsWslInstalled && IsHyperVInstalled)
                {

                    WslInstallationComplete = false;

                    await RefreshStatusAsync(silent: true);

                    ShowServerControls = IsDistributionInstalled;

                    IsLoading = false;

                    _statusTimer.Start();
                    _schedulerService.Start();

                    if (!string.IsNullOrWhiteSpace(_appSettings.PeerId))
                    {

                        await FetchNodeInfoAsync(silent: true);

                        _nodeInfoTimer.Start();
                    }

                    if (_appSettings.AutoStartServer &&
                        IsDistributionInstalled &&
                        CurrentStatus == WslServerStatus.Stopped)
                    {

                        await Task.Delay(500);
                        await RefreshStatusAsync(silent: true);

                        if (CurrentStatus == WslServerStatus.Stopped)
                        {
                            await StartServerAsync();
                        }
                    }
                }
                else
                {

                    CurrentStatus = WslServerStatus.NotInstalled;
                    IsDistributionInstalled = false;
                    IsLoading = false;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error during initialization: {ex.Message}";
                CurrentStatus = WslServerStatus.NotInstalled;
                IsLoading = false;
            }
        }

        private async Task RefreshStatusAsync(bool silent = false)
        {
            if (IsLoading && !silent)
                return;

            if (!silent)
            {
                IsLoading = true;
            }

            try
            {
                var serverInfo = await _wslService.GetServerStatusAsync(TargetDistribution);

                if (CurrentStatus != serverInfo.Status)
                {
                    CurrentStatus = serverInfo.Status;
                }

                if (DistributionVersion != serverInfo.Version)
                {
                    DistributionVersion = serverInfo.Version;
                }

                if (WslUptime != serverInfo.Uptime)
                {
                    WslUptime = serverInfo.Uptime;
                }

                bool newIsDistributionInstalled = serverInfo.IsInstalled;
                if (IsDistributionInstalled != newIsDistributionInstalled)
                {
                    IsDistributionInstalled = newIsDistributionInstalled;
                }

                if (IsDistributionInstalled && CurrentStatus == WslServerStatus.Running)
                {
                    var serviceStatus = await _wslService.GetCjdnsServiceStatusAsync(TargetDistribution);
                    CjdnsServiceStatus = serviceStatus;

                    var internetConnectivity = await _wslService.CheckInternetConnectivityAsync(TargetDistribution);
                    HasInternetConnection = internetConnectivity;
                }
                else
                {

                    CjdnsServiceStatus = null;
                    HasInternetConnection = false;
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    StatusMessage = $"Error refreshing status: {ex.Message}";
                }
            }
            finally
            {
                if (!silent)
                {
                    IsLoading = false;
                }
            }
        }

        private async Task StartServerAsync()
        {
            IsLoading = true;
            StatusMessage = $"Starting {TargetDistribution}...";

            try
            {
                var success = await _wslService.StartServerAsync(TargetDistribution);
                if (success)
                {
                    StatusMessage = $"{TargetDistribution} started successfully.";
                    await Task.Delay(1500);
                    await RefreshStatusAsync(silent: false);
                }
                else
                {
                    StatusMessage = $"Failed to start {TargetDistribution}.";
                    await RefreshStatusAsync(silent: false);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error starting server: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task StopServerAsync()
        {
            IsLoading = true;
            StatusMessage = $"Stopping {TargetDistribution}...";

            bool timerWasRunning = _statusTimer.IsEnabled;
            if (timerWasRunning)
            {
                _statusTimer.Stop();
            }

            try
            {
                var success = await _wslService.StopServerAsync(TargetDistribution);
                if (success)
                {
                    StatusMessage = $"{TargetDistribution} stopped successfully.";

                    await RefreshStatusAsync(silent: false);
                }
                else
                {
                    StatusMessage = $"Failed to stop {TargetDistribution}.";
                    await RefreshStatusAsync(silent: false);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error stopping server: {ex.Message}";
            }
            finally
            {
                IsLoading = false;

                if (timerWasRunning)
                {
                    _statusTimer.Start();
                }
            }
        }

        private Task InstallWslAsync()
        {

            ShowWslInstallConfirmation = true;
            return Task.CompletedTask;
        }

        private void CancelInstallWsl()
        {
            ShowWslInstallConfirmation = false;
        }

        private async Task ConfirmInstallWslAsync()
        {

            ShowWslInstallConfirmation = false;

            bool needsHyperV = NeedsHyperVInstall;
            bool needsWsl = NeedsWslInstall;

            IsInstallingWsl = true;
            WslInstallationStatus = "Preparing to install dependencies...";

            var componentsToInstall = new System.Collections.Generic.List<string>();
            if (needsHyperV)
                componentsToInstall.Add("Hyper-V");
            if (needsWsl)
                componentsToInstall.Add("WSL");
            string componentsText = string.Join(" and ", componentsToInstall);

            StatusMessage = $"Installing {componentsText}... This may take several minutes.";

            try
            {
                await Task.Delay(500);

                if (needsHyperV && needsWsl)
                {
                    WslInstallationStatus = "Enabling Hyper-V and installing WSL...";
                }
                else if (needsHyperV)
                {
                    WslInstallationStatus = "Enabling Hyper-V...";
                }
                else if (needsWsl)
                {
                    WslInstallationStatus = "Installing WSL...";
                }

                var success = await _wslService.InstallWslAsync();

                if (success)
                {
                    await Task.Delay(1000);

                    StatusMessage = "Installation complete. Restart required.";
                    WslInstallationStatus = "✓ Dependencies installed successfully";
                    WslInstallationComplete = true;
                    RaiseCommandsCanExecuteChanged();
                }
                else
                {
                    MessageBox.Show(
                        "Failed to install dependencies. Please try installing manually or check if you have administrator privileges.",
                        "Installation Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    StatusMessage = "Installation failed.";
                    WslInstallationStatus = "";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Error during installation: {ex.Message}",
                    "Installation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                StatusMessage = $"Error: {ex.Message}";
                WslInstallationStatus = "";
            }
            finally
            {
                IsInstallingWsl = false;
            }
        }

        private void ShowRestartConfirmationDialog()
        {

            ShowRestartConfirmation = true;
        }

        private void CancelRestart()
        {
            ShowRestartConfirmation = false;
        }

        private void ConfirmRestart()
        {

            ShowRestartConfirmation = false;

            try
            {

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "shutdown",
                    Arguments = "/r /t 0 /c \"Restarting to complete WSL installation\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var process = System.Diagnostics.Process.Start(processStartInfo))
                {
                    // Process will be disposed when system shuts down
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to restart the computer: {ex.Message}\n\nPlease restart manually to complete WSL installation.",
                    "Restart Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private async Task DeployServerAsync()
        {
            // Check for existing WSL distributions
            var existingDistributions = await _wslService.GetInstalledDistributionsAsync();
            var otherDistributions = existingDistributions
                .Where(d => !d.Equals(TargetDistribution, StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (otherDistributions.Length > 0)
            {
                // Show warning about bridged network impact
                var distributionsList = string.Join("\n  • ", otherDistributions);
                var result = MessageBox.Show(
                    $"⚠️ Warning: Existing WSL Distributions Detected\n\n" +
                    $"The following WSL distributions are currently installed:\n  • {distributionsList}\n\n" +
                    $"Deploying PKTWinNode will enable a bridged network configuration that will replace the current NAT network. " +
                    $"This may cause network interruptions for the existing WSL distributions listed above.\n\n" +
                    $"To restore network connectivity to these distributions after deployment, you will need to configure them with static IP addresses.\n\n" +
                    $"Do you want to continue with the deployment?",
                    "Bridged Network Warning",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != System.Windows.MessageBoxResult.Yes)
                {
                    return;
                }
            }

            // Proceed with normal deployment confirmation
            ShowDeployConfirmation = true;
        }

        private void CancelDeploy()
        {
            ShowDeployConfirmation = false;
        }

        private async Task ConfirmDeployAsync()
        {

            ShowDeployConfirmation = false;

            if (!NetworkValidationHelper.IsValidStaticIpAddress(_appSettings.StaticIpAddress, out string staticIpError))
            {
                MessageBox.Show(
                    $"Static IP configuration is required for deployment.\n\n{staticIpError}\n\nPlease configure a valid static IP address in Settings → Network.",
                    "Static IP Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!NetworkValidationHelper.IsValidDnsServers(_appSettings.DnsServers, out string dnsError))
            {
                MessageBox.Show(
                    $"Valid DNS servers are required for deployment.\n\n{dnsError}\n\nPlease configure valid DNS servers in Settings → Network.",
                    "DNS Configuration Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!NetworkValidationHelper.IsValidGateway(_appSettings.Gateway, out string gatewayError))
            {
                MessageBox.Show(
                    $"The gateway configuration is invalid.\n\n{gatewayError}\n\nPlease fix it in Settings → Network.",
                    "Gateway Configuration Invalid",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            IsLoading = true;
            IsDeploying = true;
            DeploymentComplete = false;
            DeploymentFailed = false;
            DeploymentProgress = 0;
            DeploymentStatus = "Starting deployment...";
            StatusMessage = $"Deploying {TargetDistribution}... This may take several minutes.";

            await Task.Delay(100);

            try
            {
                string lastStatus = string.Empty;
                int lastProgress = 0;

                var success = await Task.Run(async () =>
                {
                    return await _wslService.DeployDistributionAsync(
                        TargetDistribution,
                        _appSettings.WslUsername,
                        _appSettings.WslPassword,
                        _appSettings.PeerId,
                        _appSettings.CjdnsPort,
                        _appSettings.StaticIpAddress,
                        (status, progress) =>
                        {
                            lastStatus = status;
                            lastProgress = progress;

                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                DeploymentStatus = status;
                                DeploymentProgress = progress;
                            });
                        });
                });

                if (success)
                {
                    DeploymentProgress = 100;
                    DeploymentStatus = $"🎉 {TargetDistribution} has been deployed successfully!";
                    DeploymentComplete = true;
                    StatusMessage = $"{TargetDistribution} deployed successfully.";

                    await RefreshStatusAsync(silent: true);
                }
                else
                {
                    DeploymentStatus = !string.IsNullOrWhiteSpace(lastStatus)
                        ? lastStatus
                        : "❌ Deployment failed. Please check the logs for more details.";
                    DeploymentFailed = true;
                    StatusMessage = DeploymentStatus;
                }
            }
            catch (Exception ex)
            {
                DeploymentStatus = $"❌ Error: {ex.Message}";
                DeploymentFailed = true;
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;

            }
        }

        private async Task ContinueAfterDeploymentAsync()
        {

            bool wasSuccessful = DeploymentComplete;

            IsDeploying = false;
            DeploymentComplete = false;
            DeploymentFailed = false;

            if (wasSuccessful)
            {

                ShowServerControls = true;

                await StartServerAsync();
            }
        }

        private void UpdateStatusMessage()
        {
            StatusMessage = CurrentStatus switch
            {
                WslServerStatus.Running => "Server is running",
                WslServerStatus.Stopped => "Server is stopped",
                WslServerStatus.NotInstalled => "Server is not installed",
                WslServerStatus.Unknown => "Status unknown",
                _ => "Unknown status"
            };
        }

        private bool CanStartServer()
        {
            return !IsLoading && IsWslInstalled && IsDistributionInstalled &&
                   (CurrentStatus == WslServerStatus.Stopped);
        }

        private bool CanStopServer()
        {
            return !IsLoading && IsWslInstalled && IsDistributionInstalled && CurrentStatus == WslServerStatus.Running;
        }

        private bool CanDeployServer()
        {

            return !IsLoading && IsWslInstalled && IsHyperVInstalled && !IsRestartPending && !IsDistributionInstalled &&
                   !string.IsNullOrWhiteSpace(_appSettings.PeerId) &&
                   NetworkValidationHelper.IsValidStaticIpAddress(_appSettings.StaticIpAddress, out _);
        }

        private void RaiseCommandsCanExecuteChanged()
        {
            (StartServerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopServerCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (InstallWslCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeployServerCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OpenSettings()
        {
            _statusTimer.Stop();

            var settingsView = new SettingsView(_settingsService, _wslService, _updateService);

            if (settingsView.ShowDialog() == true)
            {

                _appSettings = _settingsService.LoadSettings();

                IsPeerIdConfigured = !string.IsNullOrWhiteSpace(_appSettings.PeerId);

                OnPropertyChanged(nameof(IsNetworkConfigured));
                OnPropertyChanged(nameof(IsCjdnsPortConfigured));
                OnPropertyChanged(nameof(IsFullyConfigured));

                _ = Task.Run(async () =>
                {
                    await RefreshStatusAsync(silent: true);

                    if (!IsDistributionInstalled)
                    {
                        ShowServerControls = false;
                    }
                });

                if (!string.IsNullOrWhiteSpace(_appSettings.PeerId))
                {

                    _ = FetchNodeInfoAsync(silent: true);

                    if (!_nodeInfoTimer.IsEnabled)
                    {
                        _nodeInfoTimer.Start();
                    }
                }
                else
                {

                    _nodeInfoTimer.Stop();
                    PktNodeInfo = null;
                    NodeInfoError = string.Empty;
                }

                RaiseCommandsCanExecuteChanged();
            }

            _statusTimer.Start();
        }

        private async Task FetchNodeInfoAsync(bool silent = false)
        {

            if (!silent)
            {
                IsLoadingNodeInfo = true;
            }

            if (!silent)
            {
                NodeInfoError = string.Empty;
            }

            try
            {

                var peerId = _appSettings.PeerId;

                if (string.IsNullOrWhiteSpace(peerId))
                {
                    if (!silent)
                    {
                        NodeInfoError = "Peer ID is not configured. Please set it in Settings.";
                        PktNodeInfo = null;
                    }
                    return;
                }

                if (!peerId.StartsWith("PUB_PKT_", StringComparison.OrdinalIgnoreCase))
                {
                    if (!silent)
                    {
                        NodeInfoError = "Invalid Peer ID format. Expected format: PUB_PKT_XX";
                        PktNodeInfo = null;
                    }
                    return;
                }

                var numberString = peerId.Substring(8);

                if (!int.TryParse(numberString, out int decimalValue))
                {
                    if (!silent)
                    {
                        NodeInfoError = "Invalid Peer ID number format.";
                        PktNodeInfo = null;
                    }
                    return;
                }

                var hexValue = decimalValue.ToString("X");

                var apiUrl = $"https://app.pkt.cash/api/v1/infra/cjdns/0x{hexValue}";

                var response = await _httpClient.GetAsync(apiUrl);

                if (!response.IsSuccessStatusCode)
                {
                    if (!silent)
                    {
                        NodeInfoError = $"API request failed with status: {response.StatusCode}";
                    }
                    return;
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                var nodeInfo = JsonSerializer.Deserialize<PktNodeInfo>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (nodeInfo != null)
                {
                    PktNodeInfo = nodeInfo;

                    if (!silent)
                    {
                        NodeInfoError = string.Empty;
                    }
                }
                else
                {
                    if (!silent)
                    {
                        NodeInfoError = "Failed to parse API response.";
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                if (!silent)
                {
                    NodeInfoError = $"Network error: {ex.Message}";
                }
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    NodeInfoError = $"Error fetching node info: {ex.Message}";
                }
            }
            finally
            {
                if (!silent)
                {
                    IsLoadingNodeInfo = false;
                }
            }
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _statusTimer?.Stop();
            _nodeInfoTimer?.Stop();
            _schedulerService?.Dispose();
            _httpClient?.Dispose();
            _disposed = true;

            GC.SuppressFinalize(this);
        }
    }
}
