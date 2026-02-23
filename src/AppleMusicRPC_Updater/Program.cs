using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AppleMusicRPC_Updater
{
    class Program
    {
        private static readonly string GitHubRepoOwner = "Kendral444";
        private static readonly string GitHubRepoName = "Apple-Music-RPC";
        private static readonly string InstallDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppleMusicRPC");
        private static readonly string ExecutableName = "Apple Music RPC.exe";
        private static readonly string VersionFile = Path.Combine(InstallDirectory, "version.txt");
        private static readonly string RegistryKeyName = "AppleMusicRPC";

        static async Task Main(string[] args)
        {
            Console.WriteLine("[Apple Music RPC - AutoUpdater] Initializing...");
            EnsureInstallDirectoryExists();
            AddSelfToStartup();

            string currentVersion = GetCurrentInstalledVersion();
            Console.WriteLine($"[Updater] Current Installed Version: {currentVersion}");

            try
            {
                var releaseInfo = await CheckForUpdates();
                if (releaseInfo != null && releaseInfo.TagName != currentVersion)
                {
                    Console.WriteLine($"[Updater] New version found: {releaseInfo.TagName}. Starting UPDATE protocol.");
                    await PerformUpdate(releaseInfo);
                }
                else
                {
                    Console.WriteLine("[Updater] Application is up to date or no connection.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Updater] Update check failed: {ex.Message}. Bypassing.");
            }

            LaunchCoreApplication();
        }

        private static void EnsureInstallDirectoryExists()
        {
            if (!Directory.Exists(InstallDirectory))
            {
                Console.WriteLine($"[Updater] Creating directory: {InstallDirectory}");
                Directory.CreateDirectory(InstallDirectory);
            }
        }

        private static void AddSelfToStartup()
        {
            try
            {
                string exePath = Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return;

                RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (rk != null)
                {
                    string existingValue = rk.GetValue(RegistryKeyName) as string;
                    if (existingValue != $"\"{exePath}\"")
                    {
                         rk.SetValue(RegistryKeyName, $"\"{exePath}\"");
                         Console.WriteLine("[Updater] Registered in Windows Startup.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Updater-Error] Failed to set registry key: {ex.Message}");
            }
        }

        private static string GetCurrentInstalledVersion()
        {
            if (File.Exists(VersionFile))
            {
                return File.ReadAllText(VersionFile).Trim();
            }
            return "v0.0";
        }

        private static async Task<GitHubRelease> CheckForUpdates()
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AppleMusicRPC_Updater", "1.0"));
                string url = $"https://api.github.com/repos/{GitHubRepoOwner}/{GitHubRepoName}/releases/latest";

                HttpResponseMessage response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var release = JsonSerializer.Deserialize(json, AppJsonSerializerContext.Default.GitHubRelease);
                    
                    if (release != null && release.Assets != null && release.Assets.Length > 0)
                    {
                        return release;
                    }
                }
            }
            return null;
        }

        private static async Task PerformUpdate(GitHubRelease release)
        {
            string zipUrl = null;
            foreach (var asset in release.Assets)
            {
                if (asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    zipUrl = asset.BrowserDownloadUrl;
                    break;
                }
            }

            if (zipUrl == null)
            {
                Console.WriteLine("[Updater] No valid .zip asset found in the latest release. Aborting update.");
                return;
            }

            KillRunningProcesses();

            string tempZipPath = Path.Combine(Path.GetTempPath(), "AppleMusicRPC_Update.zip");
            
            Console.WriteLine($"[Updater] Downloading {release.TagName}...");
            using (HttpClient client = new HttpClient())
            {
                byte[] fileBytes = await client.GetByteArrayAsync(zipUrl);
                File.WriteAllBytes(tempZipPath, fileBytes);
            }

            Console.WriteLine("[Updater] Extracting package...");
            
            // On ecrase l'existant sans poser de question
            using (ZipArchive archive = ZipFile.OpenRead(tempZipPath))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string destinationPath = Path.GetFullPath(Path.Combine(InstallDirectory, entry.FullName));

                    if (destinationPath.StartsWith(InstallDirectory, StringComparison.Ordinal))
                    {
                        if (string.IsNullOrEmpty(entry.Name)) // C'est un dossier
                        {
                            Directory.CreateDirectory(destinationPath);
                        }
                        else
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                            entry.ExtractToFile(destinationPath, true); // true = overwrite
                        }
                    }
                }
            }

            // Nettoyage tmp
            File.Delete(tempZipPath);
            
            // Mise a jour de la version
            File.WriteAllText(VersionFile, release.TagName);
            Console.WriteLine($"[Updater] Update to {release.TagName} completed successfully.");
        }

        private static void KillRunningProcesses()
        {
            string[] processesToKill = { "Apple Music RPC", "MediaExtractor", "core-bin" };
            foreach (var procName in processesToKill)
            {
                var processes = Process.GetProcessesByName(procName);
                foreach (var process in processes)
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit();
                        Console.WriteLine($"[Updater] Killed obsolete process: {procName}");
                    }
                    catch { /* Ignore acces refusé */ }
                }
            }
        }

        private static void LaunchCoreApplication()
        {
            string exePath = Path.Combine(InstallDirectory, ExecutableName);
            if (File.Exists(exePath))
            {
                Console.WriteLine($"[Updater] Launching Core Application: {exePath}");
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = InstallDirectory,
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Hidden // Furtivité
                };
                Process.Start(startInfo);
            }
            else
            {
                Console.WriteLine($"[Updater-Error] Core Application not found at: {exePath}");
            }
        }
    }

    // Classes pour desérialiser l'API Github
    public class GitHubRelease
    {
        [System.Text.Json.Serialization.JsonPropertyName("tag_name")]
        public string TagName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("assets")]
        public GitHubAsset[] Assets { get; set; }
    }

    public class GitHubAsset
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string Name { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; }
    }

    [System.Text.Json.Serialization.JsonSerializable(typeof(GitHubRelease))]
    [System.Text.Json.Serialization.JsonSerializable(typeof(GitHubAsset))]
    public partial class AppJsonSerializerContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }
}
