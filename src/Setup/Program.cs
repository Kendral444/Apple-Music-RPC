using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Setup;

/// <summary>
/// Installeur bootstrap — télécharge l'Updater depuis la dernière release GitHub,
/// l'installe dans %APPDATA%\AppleMusicRPC et lance l'application.
/// </summary>
static class Program
{
    private const string RepoOwner   = "Kendral444";
    private const string RepoName    = "apple-music-rpc";
    private const string UpdaterExe  = "Apple Music RPC Updater.exe";
    private const string AppName     = "Apple Music RPC";
    private const string UninstallKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\AppleMusicRPC";

    private static readonly string InstallDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppleMusicRPC"
    );

    static async Task Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = $"Installation — {AppName}";

        // Déjà installé ?
        var updaterPath = Path.Combine(InstallDir, UpdaterExe);
        if (File.Exists(updaterPath))
        {
            Console.WriteLine("[Setup] L'application est déjà installée. Lancement de la mise à jour...");
            Process.Start(new ProcessStartInfo { FileName = updaterPath, UseShellExecute = true });
            return;
        }

        Console.WriteLine($"[Setup] Installation de {AppName}...");
        Console.WriteLine($"[Setup] Répertoire : {InstallDir}");

        Directory.CreateDirectory(InstallDir);

        // 1. Récupérer la dernière release
        Console.WriteLine("[Setup] Vérification de la dernière version...");
        var release = await FetchLatestReleaseAsync();
        if (release is null)
        {
            Console.WriteLine("[Setup] Erreur : impossible de contacter GitHub. Vérifiez votre connexion.");
            Pause();
            return;
        }

        Console.WriteLine($"[Setup] Version : {release.TagName}");

        // 2. Télécharger l'Updater
        var asset = release.Assets?.FirstOrDefault(a =>
            a.Name?.Equals(UpdaterExe, StringComparison.OrdinalIgnoreCase) == true);

        if (asset?.DownloadUrl is null)
        {
            Console.WriteLine("[Setup] Erreur : asset introuvable dans la release.");
            Pause();
            return;
        }

        Console.WriteLine("[Setup] Téléchargement de l'updater...");
        using var http = new HttpClient();
        var bytes = await http.GetByteArrayAsync(asset.DownloadUrl);
        File.WriteAllBytes(updaterPath, bytes);
        Console.WriteLine("[Setup] Updater installé.");

        // 3. Enregistrer dans Ajout/Suppression de programmes
        RegisterUninstallEntry(updaterPath, release.TagName ?? "");
        Console.WriteLine("[Setup] Entrée Ajout/Suppression de programmes créée.");

        // 4. Raccourci dans le menu Démarrer
        CreateStartMenuShortcut(updaterPath);
        Console.WriteLine("[Setup] Raccourci menu Démarrer créé.");

        // 5. Lancer l'Updater (qui télécharge le core + lance l'app)
        Console.WriteLine("[Setup] Lancement de l'application...");
        Process.Start(new ProcessStartInfo
        {
            FileName        = updaterPath,
            UseShellExecute = true,
            WindowStyle     = ProcessWindowStyle.Hidden
        });

        Console.WriteLine("[Setup] Installation terminée ✓");
        await Task.Delay(2000); // laisse le temps de lire
    }

    // ── Ajout/Suppression de programmes ──────────────────────────────────────
    private static void RegisterUninstallEntry(string updaterPath, string version)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(UninstallKey, writable: true);
            key.SetValue("DisplayName",          AppName);
            key.SetValue("DisplayVersion",       version);
            key.SetValue("Publisher",            "Kendral444");
            key.SetValue("InstallLocation",      InstallDir);
            key.SetValue("UninstallString",      $"\"{updaterPath}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{updaterPath}\" --uninstall --quiet");
            key.SetValue("NoModify",             1, RegistryValueKind.DWord);
            key.SetValue("NoRepair",             1, RegistryValueKind.DWord);
            key.SetValue("EstimatedSize",        20480, RegistryValueKind.DWord); // ~20 MB en KB
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Setup] Avertissement : impossible d'enregistrer dans le registre ({ex.Message})");
        }
    }

    // ── Raccourci menu Démarrer ───────────────────────────────────────────────
    private static void CreateStartMenuShortcut(string updaterPath)
    {
        try
        {
            var startMenu = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs", AppName);
            Directory.CreateDirectory(startMenu);

            // Créer le raccourci via PowerShell (pas de dépendance COM)
            var shortcutPath = Path.Combine(startMenu, $"{AppName}.lnk");
            var script = $"""
                $ws = New-Object -ComObject WScript.Shell
                $sc = $ws.CreateShortcut('{shortcutPath}')
                $sc.TargetPath = '{updaterPath}'
                $sc.WorkingDirectory = '{InstallDir}'
                $sc.Description = 'Apple Music Rich Presence pour Discord'
                $sc.Save()
                """;
            Process.Start(new ProcessStartInfo
            {
                FileName        = "powershell.exe",
                Arguments       = $"-NoProfile -Command \"{script.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow  = true
            })?.WaitForExit(5000);
        }
        catch { /* pas critique */ }
    }

    private static async Task<GitHubRelease?> FetchLatestReleaseAsync()
    {
        try
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AppleMusicRPC-Setup", "1.0"));
            var json = await http.GetStringAsync(
                $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
            return JsonSerializer.Deserialize(json, SetupJsonCtx.Default.GitHubRelease);
        }
        catch { return null; }
    }

    private static void Pause()
    {
        Console.WriteLine("\nAppuyez sur une touche pour fermer...");
        Console.ReadKey(intercept: true);
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
internal partial class SetupJsonCtx : JsonSerializerContext { }
