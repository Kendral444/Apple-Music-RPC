using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Updater;

static class Program
{
    private const string RepoOwner    = "Kendral444";
    private const string RepoName     = "apple-music-rpc";
    private const string AppExeName   = "Apple Music RPC.exe";
    private const string StartupKey   = "AppleMusicRPC";
    private const string UninstallReg = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AppleMusicRPC";
    private const string StartupReg   = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

    private static readonly string InstallDir  = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppleMusicRPC");
    private static readonly string VersionFile = Path.Combine(InstallDir, "version.txt");
    private static readonly string MainExePath = Path.Combine(InstallDir, AppExeName);

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        // ── Mode désinstallation ───────────────────────────────────────────────
        if (args.Contains("--uninstall"))
        {
            bool quiet = args.Contains("--quiet");
            Uninstall(quiet);
            return;
        }

        // ── Mode normal : mise à jour + lancement ──────────────────────────────
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

    // ── Désinstallation ───────────────────────────────────────────────────────
    private static void Uninstall(bool quiet)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "Désinstallation — Apple Music RPC";
        Console.WriteLine("[Uninstall] Désinstallation de Apple Music RPC...");

        // 1. Tuer les processus en cours
        KillAllProcesses();
        Console.WriteLine("[Uninstall] Processus arrêtés.");

        // 2. Supprimer l'entrée de démarrage Windows
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupReg, writable: true);
            key?.DeleteValue(StartupKey, throwOnMissingValue: false);
            Console.WriteLine("[Uninstall] Démarrage automatique supprimé.");
        }
        catch { }

        // 3. Supprimer l'entrée Ajout/Suppression de programmes
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(UninstallReg, throwOnMissingSubKey: false);
            Console.WriteLine("[Uninstall] Entrée Programmes supprimée.");
        }
        catch { }

        // 4. Supprimer le raccourci menu Démarrer
        try
        {
            var shortcutDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs", "Apple Music RPC");
            if (Directory.Exists(shortcutDir))
            {
                Directory.Delete(shortcutDir, recursive: true);
                Console.WriteLine("[Uninstall] Raccourci menu Démarrer supprimé.");
            }
        }
        catch { }

        // 5. Planifier la suppression du répertoire d'installation + auto-suppression
        //    (on ne peut pas supprimer le répertoire depuis l'intérieur de celui-ci)
        var selfExe   = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        var scriptPath = Path.Combine(Path.GetTempPath(), "AppleMusicRPC_uninstall.cmd");

        File.WriteAllText(scriptPath, $"""
            @echo off
            ping 127.0.0.1 -n 3 > nul
            rmdir /s /q "{InstallDir}"
            del /f /q "{selfExe}"
            del /f /q "%~f0"
            """);

        Process.Start(new ProcessStartInfo
        {
            FileName        = "cmd.exe",
            Arguments       = $"/c \"{scriptPath}\"",
            UseShellExecute = true,
            WindowStyle     = ProcessWindowStyle.Hidden
        });

        Console.WriteLine("[Uninstall] Désinstallation complète. À bientôt !");

        if (!quiet)
        {
            Console.WriteLine("\nAppuyez sur une touche pour fermer...");
            Console.ReadKey(intercept: true);
        }
    }

    // ── Mise à jour ───────────────────────────────────────────────────────────
    private static void RegisterStartup()
    {
        try
        {
            var self = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(self)) return;
            using var key = Registry.CurrentUser.OpenSubKey(StartupReg, writable: true);
            if (key?.GetValue(StartupKey) as string != $"\"{self}\"")
                key?.SetValue(StartupKey, $"\"{self}\"");
        }
        catch { }
    }

    private static async Task<GitHubRelease?> FetchLatestReleaseAsync()
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AppleMusicRPC", "1.0"));
        var json = await http.GetStringAsync(
            $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
        return JsonSerializer.Deserialize(json, UpdaterJsonCtx.Default.GitHubRelease);
    }

    private static async Task ApplyUpdateAsync(GitHubRelease release)
    {
        var zipAsset = release.Assets?.FirstOrDefault(a =>
            a.Name?.Equals("AppleMusicRPC.zip", StringComparison.OrdinalIgnoreCase) == true);

        if (zipAsset?.DownloadUrl is null)
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
            foreach (var proc in Process.GetProcessesByName(name))
                try { proc.Kill(); proc.WaitForExit(3000); } catch { }
    }

    private static void KillAllProcesses()
    {
        foreach (var name in new[] { "Apple Music RPC", "AppleMusicRPC", "Apple Music RPC Updater" })
            foreach (var proc in Process.GetProcessesByName(name))
                try { if (proc.Id != Environment.ProcessId) { proc.Kill(); proc.WaitForExit(3000); } } catch { }
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
            FileName        = MainExePath,
            WorkingDirectory = InstallDir,
            UseShellExecute = true,
            WindowStyle     = ProcessWindowStyle.Hidden
        });
    }
}

public class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string?        TagName { get; set; }
    [JsonPropertyName("assets")]   public GitHubAsset[]? Assets  { get; set; }
}

public class GitHubAsset
{
    [JsonPropertyName("name")]                  public string? Name        { get; set; }
    [JsonPropertyName("browser_download_url")] public string? DownloadUrl { get; set; }
}

[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(GitHubAsset))]
internal partial class UpdaterJsonCtx : JsonSerializerContext { }
