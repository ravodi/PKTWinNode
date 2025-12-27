using System.Diagnostics;
using System.IO;
using System.Net.Http;
using PKTWinNode.Models;

namespace PKTWinNode.Services
{
    public class WslService : IWslService
    {
        private readonly ISettingsService _settingsService;

        public WslService(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        public async Task<bool> IsWslInstalledAsync()
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wsl.exe",
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    return true;
                }

                using var process2 = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wsl.exe",
                        Arguments = "--list",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                process2.Start();
                await process2.WaitForExitAsync();

                return process2.ExitCode == 0;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to check if WSL is installed.\n\nError: {ex.Message}",
                    "WSL Check Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> IsHyperVInstalledAsync()
        {
            try
            {

                var checkHyperVCmd = "Get-Command Get-VMSwitch -ErrorAction SilentlyContinue";
                var checkResult = await ExecutePowerShellCommandAsync(checkHyperVCmd);

                return !string.IsNullOrWhiteSpace(checkResult.Output) && checkResult.Output.Contains("Get-VMSwitch");
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to check if Hyper-V is installed.\n\nError: {ex.Message}",
                    "Hyper-V Check Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> IsRestartPendingAsync()
        {
            try
            {

                var checkRestartCmd = @"
                    $pending = $false

                    # Check Windows Update pending reboot
                    if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired') {
                        $pending = $true
                    }

                    # Check Component Based Servicing pending reboot
                    if (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending') {
                        $pending = $true
                    }

                    # Check pending file rename operations
                    $pfro = Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager' -Name 'PendingFileRenameOperations' -ErrorAction SilentlyContinue
                    if ($pfro -and $pfro.PendingFileRenameOperations) {
                        $pending = $true
                    }

                    # Check pending computer rename
                    $activeComputerName = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\ComputerName\ActiveComputerName' -ErrorAction SilentlyContinue).ComputerName
                    $pendingComputerName = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName' -ErrorAction SilentlyContinue).ComputerName
                    if ($activeComputerName -ne $pendingComputerName) {
                        $pending = $true
                    }

                    Write-Output $pending
                ";

                var result = await ExecutePowerShellCommandAsync(checkRestartCmd);

                return !string.IsNullOrWhiteSpace(result.Output) && result.Output.Trim().Equals("True", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to check if system restart is pending.\n\nError: {ex.Message}",
                    "Restart Check Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<WslServerInfo> GetServerStatusAsync(string distributionName)
        {
            var serverInfo = new WslServerInfo
            {
                Name = distributionName,
                Status = WslServerStatus.Unknown,
                IsInstalled = false
            };

            try
            {

                var listResult = await ExecuteCommandAsync("wsl", "--list --verbose");
                if (listResult.ExitCode != 0)
                {
                    serverInfo.Status = WslServerStatus.NotInstalled;
                    return serverInfo;
                }

                var output = listResult.Output;
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                foreach (var line in lines.Skip(1))
                {

                    var cleanLine = new string(line.Where(c => c != '\0' && !char.IsControl(c)).ToArray())
                        .Replace(" ", "")
                        .Trim();

                    if (string.IsNullOrWhiteSpace(cleanLine))
                        continue;

                    cleanLine = cleanLine.TrimStart('*');

                    if (cleanLine.StartsWith(distributionName, StringComparison.OrdinalIgnoreCase))
                    {
                        serverInfo.IsInstalled = true;

                        if (cleanLine.Contains("Running", StringComparison.OrdinalIgnoreCase))
                        {
                            serverInfo.Status = WslServerStatus.Running;
                        }
                        else if (cleanLine.Contains("Stopped", StringComparison.OrdinalIgnoreCase))
                        {
                            serverInfo.Status = WslServerStatus.Stopped;
                        }
                        else
                        {
                            serverInfo.Status = WslServerStatus.Stopped;
                        }

                        if (cleanLine.EndsWith("2"))
                        {
                            serverInfo.Version = "WSL 2";
                        }
                        else if (cleanLine.EndsWith("1"))
                        {
                            serverInfo.Version = "WSL 1";
                        }

                        break;
                    }
                }

                if (!serverInfo.IsInstalled)
                {
                    serverInfo.Status = WslServerStatus.NotInstalled;
                }

                if (serverInfo.Status == WslServerStatus.Running)
                {
                    try
                    {

                        var systemdUptimeCmd = "systemctl show --property=UserspaceTimestampMonotonic";
                        var systemdResult = await ExecuteWslCommandAsync(distributionName, systemdUptimeCmd, asRoot: false);

                        if (systemdResult.ExitCode == 0 && systemdResult.Output.Contains("="))
                        {
                            var timestampStr = systemdResult.Output.Split('=', 2)[1].Trim();
                            if (long.TryParse(timestampStr, out long timestampMicroseconds) && timestampMicroseconds > 0)
                            {

                                var systemUptimeCmd = "cat /proc/uptime | awk '{print $1}'";
                                var uptimeResult = await ExecuteWslCommandAsync(distributionName, systemUptimeCmd, asRoot: false);

                                if (uptimeResult.ExitCode == 0 && double.TryParse(uptimeResult.Output.Trim(), out double totalUptimeSeconds))
                                {

                                    long distributionUptimeSeconds = (long)(totalUptimeSeconds - (timestampMicroseconds / 1000000.0));
                                    if (distributionUptimeSeconds > 0)
                                    {
                                        serverInfo.Uptime = FormatUptime(distributionUptimeSeconds);
                                    }
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(serverInfo.Uptime))
                        {
                            var pid1StartCmd = "ps -p 1 -o etimes= | tr -d ' '";
                            var pid1Result = await ExecuteWslCommandAsync(distributionName, pid1StartCmd, asRoot: false);

                            if (pid1Result.ExitCode == 0 && long.TryParse(pid1Result.Output.Trim(), out long seconds) && seconds > 0)
                            {
                                serverInfo.Uptime = FormatUptime(seconds);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show(
                            $"Failed to calculate uptime for '{distributionName}'.\n\nError: {ex.Message}",
                            "Uptime Calculation Error",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                serverInfo.Status = WslServerStatus.Unknown;
                System.Windows.MessageBox.Show(
                    $"Failed to get server status for '{distributionName}'.\n\nError: {ex.Message}",
                    "Server Status Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }

            return serverInfo;
        }

        public async Task<bool> StartServerAsync(string distributionName)
        {
            try
            {

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = "wsl",
                    Arguments = $"-d {distributionName}",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    RedirectStandardInput = false
                };

                using (var process = Process.Start(processStartInfo))
                {
                    await Task.Delay(1500);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to start WSL server '{distributionName}'.\n\nError: {ex.Message}",
                    "Server Start Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> StopServerAsync(string distributionName)
        {
            try
            {

                var result = await ExecuteCommandAsync("wsl", $"--terminate {distributionName}");

                if (result.ExitCode != 0)
                {
                    return false;
                }

                await Task.Delay(2000);

                for (int i = 0; i < 3; i++)
                {
                    var statusCheck = await GetServerStatusAsync(distributionName);

                    if (statusCheck.Status == WslServerStatus.Stopped)
                    {
                        return true;
                    }

                    if (statusCheck.Status == WslServerStatus.Running)
                    {

                        await Task.Delay(1000 * (i + 1));

                        await ExecuteCommandAsync("wsl", $"--terminate {distributionName}");
                        await Task.Delay(1500);
                    }
                    else
                    {

                        return true;
                    }
                }

                var finalCheck = await GetServerStatusAsync(distributionName);
                return finalCheck.Status == WslServerStatus.Stopped ||
                       finalCheck.Status == WslServerStatus.NotInstalled;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to stop WSL server '{distributionName}'.\n\nError: {ex.Message}",
                    "Server Stop Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> InstallWslAsync()
        {
            try
            {

                var checkHyperVCmd = "Get-Command Get-VMSwitch -ErrorAction SilentlyContinue";
                var checkResult = await ExecutePowerShellCommandAsync(checkHyperVCmd);

                bool hyperVAlreadyEnabled = !string.IsNullOrWhiteSpace(checkResult.Output) && checkResult.Output.Contains("Get-VMSwitch");

                if (!hyperVAlreadyEnabled)
                {

                    var hyperVEnabledResult = await EnableHyperVSilentAsync();

                    if (hyperVEnabledResult == HyperVEnableResult.Failed)
                    {

                        System.Windows.MessageBox.Show(
                            "Hyper-V enablement failed.\n\n" +
                            "Please try enabling Hyper-V manually:\n" +
                            "1. Open 'Turn Windows features on or off'\n" +
                            "2. Enable 'Hyper-V'\n" +
                            "3. Restart your computer\n\n" +
                            "Then try installing WSL again.",
                            "Hyper-V Enablement Failed",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Error);
                        return false;
                    }

                }

                var psCommand = "Start-Process -FilePath 'wsl' -ArgumentList '--install --no-distribution' -Verb RunAs -WindowStyle Hidden -Wait";
                var result = await ExecutePowerShellCommandAsync(psCommand, requiresElevation: false);

                if (result.ExitCode == 0)
                {

                    return true;
                }

                System.Windows.MessageBox.Show(
                    $"WSL installation command failed.\n\n" +
                    $"Exit Code: {result.ExitCode}\n" +
                    $"Output: {result.Output}\n" +
                    $"Error: {result.Error}\n\n" +
                    "Please try installing WSL manually by running:\n" +
                    "wsl --install --no-distribution\n\n" +
                    "in an elevated PowerShell window.",
                    "WSL Installation Failed",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);

                return false;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"An unexpected error occurred:\n\n{ex.Message}\n\n{ex.StackTrace}",
                    "Installation Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<string[]> GetInstalledDistributionsAsync()
        {
            try
            {
                var result = await ExecuteCommandAsync("wsl", "--list --quiet");
                if (result.ExitCode != 0)
                {
                    return Array.Empty<string>();
                }

                var distributions = result.Output
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => new string(line.Where(c => c != '\0' && !char.IsControl(c)).ToArray())
                        .Replace(" ", "")
                        .Trim())
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();

                return distributions;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to get installed WSL distributions.\n\nError: {ex.Message}",
                    "Distribution Query Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return Array.Empty<string>();
            }
        }

        public async Task<bool> DeployDistributionAsync(string distributionName, string username = "pktwinnode", string password = "pktwinnode", string peerId = "", string cjdnsPort = "", string staticIp = "", Action<string, int>? progressCallback = null)
        {
            string? tempWslPath = null;

            try
            {
                progressCallback?.Invoke("Downloading Ubuntu 24.04 WSL image...", 10);

                tempWslPath = Path.Combine(Path.GetTempPath(), $"ubuntu-24.04-{Guid.NewGuid():N}.wsl");

                const string wslImageUrl = "https://releases.ubuntu.com/noble/ubuntu-24.04.3-wsl-amd64.wsl";

                using var httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };

                try
                {
                    using var response = await httpClient.GetAsync(wslImageUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    var totalBytes = response.Content.Headers.ContentLength ?? 0;
                    using var contentStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(tempWslPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var buffer = new byte[8192];
                    long totalBytesRead = 0;
                    int bytesRead;
                    int lastProgress = 10;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;

                        if (totalBytes > 0)
                        {
                            var progressPercent = 10 + (int)((double)totalBytesRead / totalBytes * 35);
                            if (progressPercent > lastProgress)
                            {
                                var downloadedMB = totalBytesRead / (1024.0 * 1024.0);
                                var totalMB = totalBytes / (1024.0 * 1024.0);
                                progressCallback?.Invoke($"Downloading Ubuntu 24.04... ({downloadedMB:F1} MB / {totalMB:F1} MB)", progressPercent);
                                lastProgress = progressPercent;
                            }
                        }
                    }

                    progressCallback?.Invoke("Download complete", 45);
                }
                catch (HttpRequestException ex)
                {
                    progressCallback?.Invoke($"Download failed: {ex.Message}", 0);
                    return false;
                }
                catch (TaskCanceledException)
                {
                    progressCallback?.Invoke("Download timed out", 0);
                    return false;
                }

                progressCallback?.Invoke("Creating PKTWinNode distribution...", 50);

                var installPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WSL",
                    distributionName);

                Directory.CreateDirectory(installPath);

                var importResult = await ExecuteCommandAsync("wsl", $"--import {distributionName} \"{installPath}\" \"{tempWslPath}\"");

                if (importResult.ExitCode != 0)
                {
                    var errorMsg = !string.IsNullOrWhiteSpace(importResult.Error) ? importResult.Error : importResult.Output;
                    progressCallback?.Invoke($"Failed to import distribution: {errorMsg}", 0);
                    return false;
                }

                progressCallback?.Invoke("Configuring user account...", 60);

                await ConfigureUserAsync(distributionName, username, password);

                if (string.IsNullOrWhiteSpace(staticIp))
                {
                    progressCallback?.Invoke("❌ Static IP address is required for deployment", 0);
                    return false;
                }

                progressCallback?.Invoke("Configuring static IP address...", 70);
                var staticIpConfigured = await ConfigureStaticIpAsync(distributionName, staticIp);

                if (!staticIpConfigured)
                {
                    progressCallback?.Invoke("❌ Failed to configure static IP address", 0);
                    return false;
                }

                progressCallback?.Invoke("Running post-deployment setup...", 75);

                await RunPostDeploymentSetupAsync(distributionName, username, password, peerId, cjdnsPort, progressCallback);

                progressCallback?.Invoke("Deployment completed successfully!", 100);

                return true;
            }
            catch (Exception ex)
            {
                progressCallback?.Invoke($"Deployment failed: {ex.Message}", 0);
                return false;
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempWslPath) && File.Exists(tempWslPath))
                {
                    try
                    {
                        File.Delete(tempWslPath);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        private async Task<bool> ConfigureUserAsync(string distributionName, string username, string password)
        {
            try
            {

                var createUserCmd = $"useradd -m -s /bin/bash {username}";
                var createUserResult = await ExecuteWslCommandAsync(distributionName, createUserCmd);

                if (createUserResult.ExitCode != 0)
                {

                }

                var setPasswordCmd = $"echo '{username}:{password}' | chpasswd";
                var setPasswordResult = await ExecuteWslCommandAsync(distributionName, setPasswordCmd);

                if (setPasswordResult.ExitCode != 0)
                {
                    return false;
                }

                var addSudoCmd = $"usermod -aG sudo {username}";
                await ExecuteWslCommandAsync(distributionName, addSudoCmd);

                var sudoersContent = $"{username} ALL=(ALL) NOPASSWD:ALL";
                var sudoersCmd = $"echo '{sudoersContent}' > /etc/sudoers.d/{username} && chmod 0440 /etc/sudoers.d/{username}";
                await ExecuteWslCommandAsync(distributionName, sudoersCmd);

                var configPath = $"/etc/wsl.conf";
                var wslConfContent = $"[user]\ndefault={username}\n\n[boot]\nsystemd=true";
                var createConfCmd = $"printf '{wslConfContent}\n' > {configPath}";
                await ExecuteWslCommandAsync(distributionName, createConfCmd);

                await ExecuteCommandAsync("wsl", $"--terminate {distributionName}");
                await Task.Delay(1000);

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to configure user account for '{distributionName}'.\n\nError: {ex.Message}",
                    "User Configuration Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        private async Task<bool> ConfigureStaticIpAsync(string distributionName, string staticIp)
        {
            try
            {

                await CreateHyperVSwitchAsync("WSLBridge");

                var wslConfigPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".wslconfig");

                var wslConfigContent = @"[wsl2]
networkingMode=bridged
vmSwitch=WSLBridge
dhcp=false
";

                if (System.IO.File.Exists(wslConfigPath))
                {
                    var existingConfig = await System.IO.File.ReadAllTextAsync(wslConfigPath);
                    if (!existingConfig.Contains("networkingMode=bridged"))
                    {

                        await System.IO.File.AppendAllTextAsync(wslConfigPath, "\n" + wslConfigContent);
                    }
                }
                else
                {
                    await System.IO.File.WriteAllTextAsync(wslConfigPath, wslConfigContent);
                }

                var getInterfaceCmd = "ip -o link show | awk -F': ' '{print $2}' | grep -v lo | head -n1";
                var interfaceResult = await ExecuteWslCommandAsync(distributionName, getInterfaceCmd, asRoot: true);
                var interfaceName = interfaceResult.Output.Trim();

                if (string.IsNullOrWhiteSpace(interfaceName))
                {
                    interfaceName = "eth0";
                }

                var ipParts = staticIp.Split('/');
                var ipAddress = ipParts[0];
                var cidr = ipParts.Length > 1 ? ipParts[1] : "24";

                var settings = _settingsService.LoadSettings();
                var gateway = settings.Gateway;

                var dnsServers = settings.DnsServers;
                if (string.IsNullOrWhiteSpace(dnsServers))
                {
                    dnsServers = "8.8.8.8, 1.1.1.1";
                }

                var dnsArray = dnsServers.Split(',')
                    .Select(dns => dns.Trim())
                    .Where(dns => !string.IsNullOrWhiteSpace(dns))
                    .ToArray();
                var dnsYaml = string.Join(", ", dnsArray.Select(dns => $"{dns}"));

                var netplanConfig = $@"network:
  version: 2
  renderer: networkd
  ethernets:
    {interfaceName}:
      addresses:
        - {ipAddress}/{cidr}
      routes:
        - to: default
          via: {gateway}
      nameservers:
        addresses: [{dnsYaml}]
      dhcp4: no";

                var netplanPath = "/etc/netplan/01-static-ip.yaml";
                var writeConfigCmd = $"cat > {netplanPath} << 'EOF'\n{netplanConfig}\nEOF";
                await ExecuteWslCommandAsync(distributionName, writeConfigCmd, asRoot: true);

                var applyCmd = "netplan apply";
                await ExecuteWslCommandAsync(distributionName, applyCmd, asRoot: true);

                var configureDnsCmd = "rm -f /etc/resolv.conf && ln -sf /run/systemd/resolve/stub-resolv.conf /etc/resolv.conf";
                await ExecuteWslCommandAsync(distributionName, configureDnsCmd, asRoot: true);

                var wslConfPath = "/etc/wsl.conf";
                var readWslConfCmd = $"cat {wslConfPath}";
                var wslConfResult = await ExecuteWslCommandAsync(distributionName, readWslConfCmd, asRoot: true);
                var wslConfContent = wslConfResult.Output;

                if (!wslConfContent.Contains("[network]"))
                {
                    var appendNetworkCmd = $"echo '\n[network]\ngenerateResolvConf = false' >> {wslConfPath}";
                    await ExecuteWslCommandAsync(distributionName, appendNetworkCmd, asRoot: true);
                }
                else if (!wslConfContent.Contains("generateResolvConf"))
                {

                    var addGenerateResolvCmd = $"sed -i '/\\[network\\]/a generateResolvConf = false' {wslConfPath}";
                    await ExecuteWslCommandAsync(distributionName, addGenerateResolvCmd, asRoot: true);
                }

                await ExecuteCommandAsync("wsl", "--shutdown");
                await Task.Delay(2000);

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to configure static IP for '{distributionName}'.\n\nError: {ex.Message}",
                    "Static IP Configuration Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        private async Task<CommandResult> ExecuteWslCommandAsync(string distributionName, string command, bool asRoot = false)
        {
            var userArg = asRoot ? "-u root " : "";
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "wsl",
                Arguments = $"-d {distributionName} {userArg}-e sh -c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            return new CommandResult
            {
                ExitCode = process.ExitCode,
                Output = output,
                Error = error
            };
        }

        private async Task<bool> RunPostDeploymentSetupAsync(string distributionName, string username, string password, string peerId, string cjdnsPort, Action<string, int>? progressCallback = null)
        {
            try
            {
                progressCallback?.Invoke("Updating package lists...", 76);

                await ExecuteWslCommandAsync(distributionName, "apt update", asRoot: true);

                progressCallback?.Invoke("Upgrading packages...", 80);

                await ExecuteWslCommandAsync(distributionName, "DEBIAN_FRONTEND=noninteractive apt upgrade -y", asRoot: true);

                progressCallback?.Invoke("Installing required packages...", 85);

                await ExecuteWslCommandAsync(distributionName, "apt install -y jq curl netplan.io", asRoot: true);

                if (!string.IsNullOrWhiteSpace(peerId))
                {
                    progressCallback?.Invoke("Installing and configuring CJDNS...", 90);

                    var cjdnsCommand = $"curl -s https://pkt.cash/special/cjdns/cjdns.sh | env CJDNS_PEERID={peerId}";

                    if (!string.IsNullOrWhiteSpace(cjdnsPort))
                    {
                        cjdnsCommand += $" CJDNS_PORT={cjdnsPort}";
                    }

                    cjdnsCommand += " CJDNS_TUN=false sh";
                    await ExecuteWslCommandAsync(distributionName, cjdnsCommand, asRoot: true);

                    progressCallback?.Invoke("CJDNS installation complete", 95);

                    progressCallback?.Invoke("Restarting WSL to apply changes...", 97);
                    await ExecuteCommandAsync("wsl", $"--terminate {distributionName}");
                    await Task.Delay(2000);

                    progressCallback?.Invoke("Finalizing deployment...", 99);
                }
                else
                {
                    progressCallback?.Invoke("Skipping CJDNS installation (no Peer ID)", 95);
                }

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to run post-deployment setup for '{distributionName}'.\n\nError: {ex.Message}",
                    "Post-Deployment Setup Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        private async Task<CommandResult> ExecuteCommandAsync(string command, string arguments, bool requiresElevation = false)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (requiresElevation)
            {
                processStartInfo.UseShellExecute = true;
                processStartInfo.Verb = "runas";
                processStartInfo.RedirectStandardOutput = false;
                processStartInfo.RedirectStandardError = false;
                processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            }

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            string output = string.Empty;
            string error = string.Empty;

            if (!requiresElevation)
            {
                output = await process.StandardOutput.ReadToEndAsync();
                error = await process.StandardError.ReadToEndAsync();
            }

            await process.WaitForExitAsync();

            return new CommandResult
            {
                ExitCode = process.ExitCode,
                Output = output,
                Error = error
            };
        }

        public async Task<CjdnsServiceStatus> GetCjdnsServiceStatusAsync(string distributionName)
        {
            var status = new CjdnsServiceStatus
            {
                IsInstalled = false,
                IsActive = false,
                IsEnabled = false,
                StatusText = "Service not found"
            };

            try
            {

                var serverInfo = await GetServerStatusAsync(distributionName);
                if (serverInfo.Status != WslServerStatus.Running)
                {
                    status.StatusText = "WSL distribution not running";
                    return status;
                }

                var systemdCheck = await ExecuteWslCommandAsync(distributionName, "systemctl is-system-running || true", asRoot: false);
                if (systemdCheck.ExitCode != 0 || string.IsNullOrWhiteSpace(systemdCheck.Output))
                {
                    status.StatusText = "Systemd not available";
                    return status;
                }

                var serviceExists = await ExecuteWslCommandAsync(distributionName, "systemctl list-unit-files cjdns-sh.service", asRoot: false);
                if (!serviceExists.Output.Contains("cjdns-sh.service"))
                {
                    status.StatusText = "Service not installed";
                    return status;
                }

                status.IsInstalled = true;

                var statusResult = await ExecuteWslCommandAsync(distributionName, "systemctl is-active cjdns-sh.service || true", asRoot: false);
                status.State = statusResult.Output.Trim();
                status.IsActive = status.State.Equals("active", StringComparison.OrdinalIgnoreCase);

                var enabledResult = await ExecuteWslCommandAsync(distributionName, "systemctl is-enabled cjdns-sh.service || true", asRoot: false);
                var enabledState = enabledResult.Output.Trim();
                status.IsEnabled = enabledState.Equals("enabled", StringComparison.OrdinalIgnoreCase);

                var detailsResult = await ExecuteWslCommandAsync(distributionName,
                    "systemctl show cjdns-sh.service --no-pager --property=ActiveState,SubState,MainPID,MemoryCurrent",
                    asRoot: false);

                if (detailsResult.ExitCode == 0)
                {
                    var lines = detailsResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('=', 2);
                        if (parts.Length == 2)
                        {
                            var key = parts[0].Trim();
                            var value = parts[1].Trim();

                            switch (key)
                            {
                                case "ActiveState":
                                    status.State = value;
                                    break;
                                case "SubState":
                                    status.SubState = value;
                                    break;
                                case "MainPID":
                                    status.MainPID = value;
                                    break;
                                case "MemoryCurrent":
                                    if (long.TryParse(value, out long memoryBytes) && memoryBytes > 0)
                                    {
                                        status.Memory = FormatBytes(memoryBytes);
                                    }
                                    break;
                            }
                        }
                    }
                }

                if (status.IsActive)
                {
                    try
                    {

                        var timestampCmd = "systemctl show cjdns-sh.service --no-pager --property=ActiveEnterTimestamp";
                        var timestampResult = await ExecuteWslCommandAsync(distributionName, timestampCmd, asRoot: false);

                        if (timestampResult.ExitCode == 0 && timestampResult.Output.Contains("="))
                        {
                            var timestamp = timestampResult.Output.Split('=', 2)[1].Trim();

                            if (!string.IsNullOrEmpty(timestamp) && !timestamp.Equals("n/a", StringComparison.OrdinalIgnoreCase))
                            {

                                var uptimeCmd = $"bash -c 'start=$(date -d \"{timestamp}\" +%s 2>/dev/null); now=$(date +%s); if [ ! -z \"$start\" ] && [ $start -gt 0 ]; then echo $((now - start)); fi'";
                                var uptimeResult = await ExecuteWslCommandAsync(distributionName, uptimeCmd, asRoot: false);

                                if (uptimeResult.ExitCode == 0 && long.TryParse(uptimeResult.Output.Trim(), out long seconds) && seconds > 0)
                                {
                                    status.Uptime = FormatUptime(seconds);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Windows.MessageBox.Show(
                            $"Failed to calculate CJDNS service uptime for '{distributionName}'.\n\nError: {ex.Message}",
                            "CJDNS Uptime Calculation Error",
                            System.Windows.MessageBoxButton.OK,
                            System.Windows.MessageBoxImage.Warning);
                    }
                }

                if (status.IsActive)
                {
                    status.StatusText = "Active (running)";
                }
                else if (status.State.Equals("inactive", StringComparison.OrdinalIgnoreCase))
                {
                    status.StatusText = "Inactive (stopped)";
                }
                else if (status.State.Equals("failed", StringComparison.OrdinalIgnoreCase))
                {
                    status.StatusText = "Failed";
                }
                else
                {
                    status.StatusText = $"State: {status.State}";
                }

            }
            catch (Exception ex)
            {
                status.StatusText = $"Error checking service status: {ex.Message}";
                System.Windows.MessageBox.Show(
                    $"Failed to get CJDNS service status for '{distributionName}'.\n\nError: {ex.Message}",
                    "CJDNS Status Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
            }

            return status;
        }

        public async Task<bool> RestartCjdnsServiceAsync(string distributionName)
        {
            try
            {

                var serverInfo = await GetServerStatusAsync(distributionName);
                if (serverInfo.Status != WslServerStatus.Running)
                {
                    return false;
                }

                await ExecuteWslCommandAsync(distributionName, "sudo systemctl restart cjdns-sh.service", asRoot: false);

                await Task.Delay(2000);

                var statusCheck = await ExecuteWslCommandAsync(distributionName, "systemctl is-active cjdns-sh.service || true", asRoot: false);
                return statusCheck.Output.Trim().Equals("active", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to restart CJDNS service on '{distributionName}'.\n\nError: {ex.Message}",
                    "CJDNS Restart Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> StartCjdnsServiceAsync(string distributionName)
        {
            try
            {

                var serverInfo = await GetServerStatusAsync(distributionName);
                if (serverInfo.Status != WslServerStatus.Running)
                {
                    return false;
                }

                var result = await ExecuteWslCommandAsync(distributionName, "sudo systemctl start cjdns-sh.service", asRoot: false);
                await Task.Delay(1000);

                return result.ExitCode == 0;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to start CJDNS service on '{distributionName}'.\n\nError: {ex.Message}",
                    "CJDNS Start Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> StopCjdnsServiceAsync(string distributionName)
        {
            try
            {

                var serverInfo = await GetServerStatusAsync(distributionName);
                if (serverInfo.Status != WslServerStatus.Running)
                {
                    return false;
                }

                var result = await ExecuteWslCommandAsync(distributionName, "sudo systemctl stop cjdns-sh.service", asRoot: false);
                await Task.Delay(1000);

                return result.ExitCode == 0;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to stop CJDNS service on '{distributionName}'.\n\nError: {ex.Message}",
                    "CJDNS Stop Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> DeleteDistributionAsync(string distributionName)
        {
            try
            {

                var installedDistros = await GetInstalledDistributionsAsync();
                var distroExists = installedDistros.Any(d => d.Equals(distributionName, StringComparison.OrdinalIgnoreCase));

                if (!distroExists)
                {
                    return false;
                }

                await ExecuteCommandAsync("wsl", $"--terminate {distributionName}");
                await Task.Delay(1000);

                var result = await ExecuteCommandAsync("wsl", $"--unregister {distributionName}");

                return result.ExitCode == 0;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to delete WSL distribution '{distributionName}'.\n\nError: {ex.Message}",
                    "Distribution Deletion Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        private async Task<bool> CreateHyperVSwitchAsync(string switchName)
        {
            try
            {

                var checkSwitchCmd = $"Get-VMSwitch -Name '{switchName}' -ErrorAction SilentlyContinue";
                var checkResult = await ExecutePowerShellCommandAsync(checkSwitchCmd);

                if (!string.IsNullOrWhiteSpace(checkResult.Output) && checkResult.Output.Contains(switchName))
                {

                    return true;
                }

                var getAdapterCmd = "Get-NetAdapter | Where-Object {$_.Status -eq 'Up' -and $_.Virtual -eq $false} | Select-Object -First 1 -ExpandProperty Name";
                var adapterResult = await ExecutePowerShellCommandAsync(getAdapterCmd);

                if (string.IsNullOrWhiteSpace(adapterResult.Output))
                {

                    var createInternalCmd = $"New-VMSwitch -Name '{switchName}' -SwitchType Internal -ErrorAction Stop";
                    var createResult = await ExecutePowerShellCommandAsync(createInternalCmd, requiresElevation: true);
                    return createResult.ExitCode == 0;
                }

                var adapterName = adapterResult.Output.Trim();

                var createSwitchCmd = $"New-VMSwitch -Name '{switchName}' -NetAdapterName '{adapterName}' -AllowManagementOS $true -ErrorAction Stop";
                var result = await ExecutePowerShellCommandAsync(createSwitchCmd, requiresElevation: true);

                return result.ExitCode == 0;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to create Hyper-V switch '{switchName}'.\n\nError: {ex.Message}",
                    "Hyper-V Switch Creation Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        private enum HyperVEnableResult
        {
            AlreadyEnabled,
            Success,
            Failed
        }

        private async Task<HyperVEnableResult> EnableHyperVSilentAsync()
        {
            try
            {

                var checkHyperVCmd = "Get-Command Get-VMSwitch -ErrorAction SilentlyContinue";
                var checkResult = await ExecutePowerShellCommandAsync(checkHyperVCmd);

                if (!string.IsNullOrWhiteSpace(checkResult.Output) && checkResult.Output.Contains("Get-VMSwitch"))
                {

                    return HyperVEnableResult.AlreadyEnabled;
                }

                var checkEditionCmd = @"
                    $isAvailable = $false
                    try {
                        $edition = (Get-WindowsEdition -Online -ErrorAction SilentlyContinue).Edition
                        if ($edition -match 'Professional|Enterprise|Education|Server') {
                            $isAvailable = $true
                        }
                    } catch {
                        $service = Get-Service -Name 'vmms' -ErrorAction SilentlyContinue
                        if ($null -ne $service) {
                            $isAvailable = $true
                        } else {
                            $isAvailable = $true
                        }
                    }
                    Write-Output $isAvailable
                ";

                var editionCheckResult = await ExecutePowerShellCommandAsync(checkEditionCmd);

                if (editionCheckResult.Output.Trim() == "False" || string.IsNullOrWhiteSpace(editionCheckResult.Output))
                {

                    return HyperVEnableResult.Failed;
                }

                var tempOutputFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hyperv_enable_{Guid.NewGuid()}.txt");
                var tempScriptFile = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"hyperv_enable_{Guid.NewGuid()}.ps1");

                var scriptContent = $@"
$outputFile = '{tempOutputFile}'
try {{
    # Use DISM to enable Hyper-V platform (all components needed for WSL and virtualization)
    # Since this script runs elevated, we can call DISM directly
    # Enable the main Hyper-V feature with all sub-components
    & dism.exe /Online /Enable-Feature /FeatureName:Microsoft-Hyper-V /All /NoRestart /Quiet | Out-Null
    $hypervExitCode = $LASTEXITCODE
    # Exit codes: 0=success, 3010=success+reboot, 50=not supported/already enabled
    if ($hypervExitCode -ne 0 -and $hypervExitCode -ne 3010 -and $hypervExitCode -ne 50) {{
        throw ""Failed to enable Hyper-V. Exit code: $hypervExitCode""
    }}

    ""SUCCESS:RestartNeeded=True"" | Out-File -FilePath $outputFile -Encoding UTF8
    exit 0
}} catch {{
    ""ERROR:$($_.Exception.Message)"" | Out-File -FilePath $outputFile -Encoding UTF8
    exit 1
}}
";

                System.IO.File.WriteAllText(tempScriptFile, scriptContent);

                var enablePsCmd = $"-ExecutionPolicy Bypass -File \"{tempScriptFile}\"";
                var enableResult = await ExecutePowerShellCommandAsyncWithScript(enablePsCmd, requiresElevation: true);

                await Task.Delay(2000);

                string capturedOutput = "";
                if (System.IO.File.Exists(tempOutputFile))
                {
                    try
                    {
                        capturedOutput = System.IO.File.ReadAllText(tempOutputFile);
                        System.IO.File.Delete(tempOutputFile);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }

                try
                {
                    if (System.IO.File.Exists(tempScriptFile))
                        System.IO.File.Delete(tempScriptFile);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }

                bool enableSuccess = enableResult.ExitCode == 0 && capturedOutput.Contains("SUCCESS");

                if (enableSuccess)
                {

                    return HyperVEnableResult.Success;
                }
                else
                {

                    System.Windows.MessageBox.Show(
                        $"Hyper-V enablement command failed.\n\n" +
                        $"Exit Code: {enableResult.ExitCode}\n" +
                        $"Captured Output: {capturedOutput}\n" +
                        $"Standard Output: {enableResult.Output}\n" +
                        $"Standard Error: {enableResult.Error}",
                        "Hyper-V Enable Command Failed",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Error);
                    return HyperVEnableResult.Failed;
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to enable Hyper-V.\n\nError: {ex.Message}",
                    "Hyper-V Enable Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return HyperVEnableResult.Failed;
            }
        }

        private async Task<CommandResult> ExecutePowerShellCommandAsyncWithScript(string arguments, bool requiresElevation = false)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (requiresElevation)
            {
                processStartInfo.UseShellExecute = true;
                processStartInfo.Verb = "runas";
                processStartInfo.RedirectStandardOutput = false;
                processStartInfo.RedirectStandardError = false;
                processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            }

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            string output = string.Empty;
            string error = string.Empty;

            if (!requiresElevation)
            {
                output = await process.StandardOutput.ReadToEndAsync();
                error = await process.StandardError.ReadToEndAsync();
            }

            await process.WaitForExitAsync();

            return new CommandResult
            {
                ExitCode = process.ExitCode,
                Output = output,
                Error = error
            };
        }

        private async Task<CommandResult> ExecutePowerShellCommandAsync(string command, bool requiresElevation = false)
        {
            var processStartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{command.Replace("\"", "`\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            if (requiresElevation)
            {
                processStartInfo.UseShellExecute = true;
                processStartInfo.Verb = "runas";
                processStartInfo.RedirectStandardOutput = false;
                processStartInfo.RedirectStandardError = false;
                processStartInfo.WindowStyle = ProcessWindowStyle.Hidden;
            }

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            string output = string.Empty;
            string error = string.Empty;

            if (!requiresElevation)
            {
                output = await process.StandardOutput.ReadToEndAsync();
                error = await process.StandardError.ReadToEndAsync();
            }

            await process.WaitForExitAsync();

            return new CommandResult
            {
                ExitCode = process.ExitCode,
                Output = output,
                Error = error
            };
        }

        private string FormatUptime(long seconds)
        {
            if (seconds < 60)
            {
                return $"{seconds} second{(seconds != 1 ? "s" : "")}";
            }
            else if (seconds < 3600)
            {
                long minutes = seconds / 60;
                return $"{minutes} minute{(minutes != 1 ? "s" : "")}";
            }
            else if (seconds < 86400)
            {
                long hours = seconds / 3600;
                long minutes = (seconds % 3600) / 60;
                if (minutes > 0)
                    return $"{hours} hour{(hours != 1 ? "s" : "")}, {minutes} minute{(minutes != 1 ? "s" : "")}";
                return $"{hours} hour{(hours != 1 ? "s" : "")}";
            }
            else
            {
                long days = seconds / 86400;
                long hours = (seconds % 86400) / 3600;
                if (hours > 0)
                    return $"{days} day{(days != 1 ? "s" : "")}, {hours} hour{(hours != 1 ? "s" : "")}";
                return $"{days} day{(days != 1 ? "s" : "")}";
            }
        }

        private string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }
            return $"{len:0.#} {sizes[order]}";
        }

        public async Task<bool> ReconfigureNetworkAsync(string distributionName, string staticIp, string gateway, string dnsServers)
        {
            try
            {

                var serverInfo = await GetServerStatusAsync(distributionName);
                if (!serverInfo.IsInstalled)
                {
                    return false;
                }

                if (serverInfo.Status == WslServerStatus.Running)
                {
                    return false;
                }

                var getInterfaceCmd = "ip -o link show | awk -F': ' '{print $2}' | grep -v lo | head -n1";
                var interfaceResult = await ExecuteWslCommandAsync(distributionName, getInterfaceCmd, asRoot: true);
                var interfaceName = interfaceResult.Output.Trim();

                if (string.IsNullOrWhiteSpace(interfaceName))
                {
                    interfaceName = "eth0";
                }

                var ipParts = staticIp.Split('/');
                var ipAddress = ipParts[0];
                var cidr = ipParts.Length > 1 ? ipParts[1] : "24";

                if (string.IsNullOrWhiteSpace(dnsServers))
                {
                    dnsServers = "8.8.8.8, 1.1.1.1";
                }

                var dnsArray = dnsServers.Split(',')
                    .Select(dns => dns.Trim())
                    .Where(dns => !string.IsNullOrWhiteSpace(dns))
                    .ToArray();
                var dnsYaml = string.Join(", ", dnsArray.Select(dns => $"{dns}"));

                var netplanConfig = $@"network:
  version: 2
  renderer: networkd
  ethernets:
    {interfaceName}:
      addresses:
        - {ipAddress}/{cidr}
      routes:
        - to: default
          via: {gateway}
      nameservers:
        addresses: [{dnsYaml}]
      dhcp4: no";

                var netplanPath = "/etc/netplan/01-static-ip.yaml";
                var writeConfigCmd = $"cat > {netplanPath} << 'EOF'\n{netplanConfig}\nEOF";
                await ExecuteWslCommandAsync(distributionName, writeConfigCmd, asRoot: true);

                var applyCmd = "netplan apply";
                await ExecuteWslCommandAsync(distributionName, applyCmd, asRoot: true);

                await Task.Delay(500);

                await StopServerAsync(distributionName);

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to reconfigure network for '{distributionName}'.\n\nError: {ex.Message}",
                    "Network Reconfiguration Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> CheckInternetConnectivityAsync(string distributionName)
        {
            try
            {
                var serverInfo = await GetServerStatusAsync(distributionName);
                if (serverInfo.Status != WslServerStatus.Running)
                {
                    return false;
                }

                var pingCmd = "ping -c 1 -W 2 8.8.8.8 > /dev/null 2>&1 && echo 'success' || echo 'failed'";
                var result = await ExecuteWslCommandAsync(distributionName, pingCmd, asRoot: false);

                return result.ExitCode == 0 && result.Output.Trim().Equals("success", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to check internet connectivity for '{distributionName}'.\n\nError: {ex.Message}",
                    "Internet Connectivity Check Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> UpdatePackagesAsync(string distributionName)
        {
            try
            {
                var serverInfo = await GetServerStatusAsync(distributionName);
                if (serverInfo.Status != WslServerStatus.Running)
                {
                    return false;
                }

                var updateResult = await ExecuteWslCommandAsync(distributionName, "apt update", asRoot: true);

                if (updateResult.ExitCode != 0)
                {
                    return false;
                }

                var upgradeResult = await ExecuteWslCommandAsync(distributionName, "DEBIAN_FRONTEND=noninteractive apt upgrade -y", asRoot: true);

                return upgradeResult.ExitCode == 0;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to update packages for '{distributionName}'.\n\nError: {ex.Message}",
                    "Package Update Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        public async Task<bool> RebootDistributionAsync(string distributionName)
        {
            try
            {
                var serverInfo = await GetServerStatusAsync(distributionName);
                if (!serverInfo.IsInstalled)
                {
                    return false;
                }

                var result = await ExecuteCommandAsync("wsl", $"--terminate {distributionName}");

                if (result.ExitCode != 0)
                {
                    return false;
                }

                await Task.Delay(2000);

                await StartServerAsync(distributionName);

                return true;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show(
                    $"Failed to reboot distribution '{distributionName}'.\n\nError: {ex.Message}",
                    "Distribution Reboot Error",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        private class CommandResult
        {
            public int ExitCode { get; set; }
            public string Output { get; set; } = string.Empty;
            public string Error { get; set; } = string.Empty;
        }
    }
}
