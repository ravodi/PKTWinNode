using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PKTWinNode.Models;

namespace PKTWinNode.Services
{
    public class UpdateService : IUpdateService
    {
        private const string GitHubApiUrl = "https://api.github.com/repos/ravodi/PKTWinNode/releases/latest";

        public string GetCurrentVersion()
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version == null)
                return "1.0.0";

            return $"{version.Major}.{version.Minor}.{version.Build}";
        }

        public async Task<UpdateInfo?> CheckForUpdatesAsync()
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };

            httpClient.DefaultRequestHeaders.Add("User-Agent", "PKTWinNode-UpdateChecker");

            var response = await httpClient.GetAsync(GitHubApiUrl);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    throw new InvalidOperationException("Too many update checks. Please try again later.");
                }
                return null;
            }

            var json = await response.Content.ReadAsStringAsync();
            var release = JsonSerializer.Deserialize<GitHubRelease>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (release == null || release.Assets.Length == 0)
                return null;

            var exeAsset = Array.Find(release.Assets, a =>
                a.Name.Equals("PKTWinNode.exe", StringComparison.OrdinalIgnoreCase));

            if (exeAsset == null)
                return null;

            var sha256Hash = await ExtractSha256HashAsync(httpClient, release, exeAsset);

            if (string.IsNullOrEmpty(sha256Hash))
            {
                throw new InvalidOperationException(
                    "Update security error: SHA256 hash not found in release. " +
                    "GitHub should automatically provide checksums for all release assets.");
            }

            var updateInfo = new UpdateInfo
            {
                Version = release.TagName.TrimStart('v'),
                DownloadUrl = exeAsset.BrowserDownloadUrl,
                FileSize = exeAsset.Size,
                ReleaseNotes = release.Body ?? "",
                PublishedAt = release.PublishedAt,
                Sha256Hash = sha256Hash
            };

            var currentVersion = GetCurrentVersion();
            if (!updateInfo.IsNewerThan(currentVersion))
                return null;

            return updateInfo;
        }

        public async Task<string?> DownloadUpdateAsync(UpdateInfo updateInfo, Action<long, long>? progressCallback = null)
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(15)
            };

            var downloadPath = Path.Combine(Path.GetTempPath(), $"PKTWinNode-Update-{Guid.NewGuid()}.tmp");

            long totalBytes;
            long totalRead = 0;

            using (var response = await httpClient.GetAsync(updateInfo.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                totalBytes = response.Content.Headers.ContentLength ?? 0;
                var buffer = new byte[8192];

                using (var fileStream = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var contentStream = await response.Content.ReadAsStreamAsync())
                {
                    while (true)
                    {
                        var bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead == 0)
                            break;

                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        progressCallback?.Invoke(totalRead, totalBytes);
                    }
                }
            }

            if (totalBytes > 0 && totalRead != totalBytes)
            {
                try
                { File.Delete(downloadPath); }
                catch { }
                throw new InvalidOperationException("Download incomplete.");
            }

            var finalPath = downloadPath + ".ready";
            byte[] fileBytes = File.ReadAllBytes(downloadPath);

            string calculatedHash;
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(fileBytes);
                calculatedHash = BitConverter.ToString(hashBytes).Replace("-", "").ToUpperInvariant();
            }

            if (!string.Equals(calculatedHash, updateInfo.Sha256Hash, StringComparison.OrdinalIgnoreCase))
            {
                try
                { File.Delete(downloadPath); }
                catch { }
                throw new InvalidOperationException("Security verification failed.");
            }

            File.WriteAllBytes(finalPath, fileBytes);
            try
            { File.Delete(downloadPath); }
            catch { }

            return finalPath;
        }

        public async Task<bool> InstallUpdateAsync(string downloadedFilePath)
        {
            var currentExePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExePath))
            {
                throw new InvalidOperationException("Unable to determine the current executable path.");
            }

            var batchPath = CreateUpdaterBatchScript(downloadedFilePath, currentExePath);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/C \"{batchPath}\"",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            Process.Start(processStartInfo);

            await Task.Delay(100);

            Environment.Exit(0);

            return true;
        }

        private async Task<string> ExtractSha256HashAsync(HttpClient httpClient, GitHubRelease release, GitHubAsset exeAsset)
        {
            if (!string.IsNullOrEmpty(exeAsset.Digest))
            {
                var digestMatch = Regex.Match(exeAsset.Digest, @"sha256:([a-fA-F0-9]{64})");
                if (digestMatch.Success)
                {
                    return digestMatch.Groups[1].Value.ToUpperInvariant();
                }
            }

            var sha256Asset = Array.Find(release.Assets, a =>
                a.Name.Equals("PKTWinNode.exe.sha256", StringComparison.OrdinalIgnoreCase));

            if (sha256Asset != null)
            {
                try
                {
                    var hashContent = await httpClient.GetStringAsync(sha256Asset.BrowserDownloadUrl);
                    var hashMatch = Regex.Match(hashContent, @"^([a-fA-F0-9]{64})");
                    if (hashMatch.Success)
                    {
                        return hashMatch.Groups[1].Value.ToUpperInvariant();
                    }
                }
                catch
                {
                }
            }

            if (!string.IsNullOrEmpty(release.Body))
            {
                var patterns = new[]
                {
                    @"SHA-?256[:\s]+([a-fA-F0-9]{64})",
                    @"sha-?256sum[:\s]+([a-fA-F0-9]{64})",
                    @"PKTWinNode\.exe[:\s]+([a-fA-F0-9]{64})",
                    @"Hash[:\s]+([a-fA-F0-9]{64})"
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(release.Body, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                    if (match.Success)
                    {
                        return match.Groups[1].Value.ToUpperInvariant();
                    }
                }
            }

            return string.Empty;
        }

        private string CreateUpdaterBatchScript(string downloadedFile, string currentExePath)
        {
            var batchContent = $@"@echo off
timeout /t 2 /nobreak >nul

:WAIT_LOOP
tasklist /FI ""IMAGENAME eq PKTWinNode.exe"" 2>NUL | find /I /N ""PKTWinNode.exe"">NUL
if ""%ERRORLEVEL%""==""0"" (
    timeout /t 1 /nobreak >nul
    goto WAIT_LOOP
)

del /F ""{currentExePath}"" 2>nul
move /Y ""{downloadedFile}"" ""{currentExePath}""

if %ERRORLEVEL% EQU 0 (
    start """" ""{currentExePath}""
)

del ""%~f0""
";

            var batchPath = Path.Combine(Path.GetTempPath(), $"PKTWinNode-Updater-{Guid.NewGuid():N}.bat");
            File.WriteAllText(batchPath, batchContent);
            return batchPath;
        }

        private class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; } = string.Empty;

            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("body")]
            public string Body { get; set; } = string.Empty;

            [JsonPropertyName("published_at")]
            public DateTime PublishedAt { get; set; }

            [JsonPropertyName("assets")]
            public GitHubAsset[] Assets { get; set; } = Array.Empty<GitHubAsset>();
        }

        private class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; } = string.Empty;

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; } = string.Empty;

            [JsonPropertyName("size")]
            public long Size { get; set; }

            [JsonPropertyName("digest")]
            public string Digest { get; set; } = string.Empty;
        }
    }
}
