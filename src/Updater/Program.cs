using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Updater;

static class Program
{
    private const string RepoOwner   = "Kendral444";
    private const string RepoName    = "apple-music-rpc";
    private const string AppExeName  = "Apple Music RPC.exe";
    private const string SelfKeyName = "AppleMusicRPC";

    private static readonly string InstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppleMusicRPC"
    );
    private static readonly string VersionFile = Path.Combine(InstallDir, "version.txt");
    private static readonly string MainExePath  = Path.Combine(InstallDir, AppExeName);

    static async Task Main()
    {
        Directory.CreateDirectory(InstallDir);
        RegisterStartup();

        string current = File.Exists(VersionFile)
            ? File.ReadAllText(VersionFile).Trim()
            : "v0.0";

        Console.WriteLine($"[Updater] Version installée : {current}");

        try
        {
            var release = await FetchLatestReleaseAsync();
            if (release?.TagName is not null && release.TagName != current)
            {
                Console.WriteLine($"[Updater] Mise à jour disponible : {release.TagName}");
                await ApplyUpdateAsync(release);
            }
            else
            {
                Console.WriteLine("[Updater] Application à jour.");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Updater] Vérification ignorée : {ex.Message}");
        }

        LaunchApp();
    }

    private static void RegisterStartup()
    {
        try
        {
            string self = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(self)) return;

            using var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key?.GetValue(SelfKeyName) as string != $"\"{self}\"")
                key?.SetValue(SelfKeyName, $"\"{self}\"");
        }
        catch { }
    }

    private static async Task<GitHubRelease?> FetchLatestReleaseAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AppleMusicRPC", "1.0"));

        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        var json = await http.GetStringAsync(url);
        return JsonSerializer.Deserialize(json, UpdaterJsonCtx.Default.GitHubRelease);
    }

    private static async Task ApplyUpdateAsync(GitHubRelease release)
    {
        var zipAsset = release.Assets?.FirstOrDefault(a =>
            a.Name.Equals("AppleMusicRPC.zip", StringComparison.OrdinalIgnoreCase));

        if (zipAsset is null)
        {
            Console.WriteLine("[Updater] Aucun asset .zip trouvé.");
            return;
        }

        KillRunningApp();

        var tmpZip = Path.Combine(Path.GetTempPath(), "AppleMusicRPC_update.zip");

        using (var http = new HttpClient())
        {
            Console.WriteLine($"[Updater] Téléchargement {release.TagName}...");
            var bytes = await http.GetByteArrayAsync(zipAsset.DownloadUrl);
            File.WriteAllBytes(tmpZip, bytes);
        }

        Console.WriteLine("[Updater] Extraction...");
        using (var zip = ZipFile.OpenRead(tmpZip))
        {
            foreach (var entry in zip.Entries)
            {
                var dest = Path.GetFullPath(Path.Combine(InstallDir, entry.FullName));
                if (!dest.StartsWith(InstallDir, StringComparison.Ordinal)) continue;

                if (string.IsNullOrEmpty(entry.Name))
                    Directory.CreateDirectory(dest);
                else
                {
                    var destDir = Path.GetDirectoryName(dest);
                    if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);
                    entry.ExtractToFile(dest, overwrite: true);
                }
            }
        }

        File.Delete(tmpZip);
        File.WriteAllText(VersionFile, release.TagName!);
        Console.WriteLine($"[Updater] Mise à jour {release.TagName} appliquée.");
    }

    private static void KillRunningApp()
    {
        foreach (var name in new[] { "Apple Music RPC", "AppleMusicRPC" })
        {
            foreach (var proc in Process.GetProcessesByName(name))
            {
                try { proc.Kill(); proc.WaitForExit(3000); } catch { }
            }
        }
    }

    private static void LaunchApp()
    {
        if (!File.Exists(MainExePath))
        {
            Console.WriteLine($"[Updater] Exécutable introuvable : {MainExePath}");
            return;
        }

        Console.WriteLine("[Updater] Lancement de l'application...");
        Process.Start(new ProcessStartInfo
        {
            FileName         = MainExePath,
            WorkingDirectory = InstallDir,
            UseShellExecute  = true,
            WindowStyle      = ProcessWindowStyle.Hidden
        });
    }
}

// --- Modèles GitHub ---
public class GitHubRelease
{
    [JsonPropertyName("tag_name")]  public string?        TagName { get; set; }
    [JsonPropertyName("assets")]    public GitHubAsset[]? Assets  { get; set; }
}

public class GitHubAsset
{
    [JsonPropertyName("name")]                  public string? Name        { get; set; }
    [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(GitHubAsset))]
internal partial class UpdaterJsonCtx : JsonSerializerContext { }
