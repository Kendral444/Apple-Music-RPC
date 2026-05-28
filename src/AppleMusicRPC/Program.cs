using AppleMusicRPC.Core;
using AppleMusicRPC.Models;
using AppleMusicRPC.Services;
using AppleMusicRPC.Tray;

namespace AppleMusicRPC;

static class Program
{
    [STAThread]
    static void Main()
    {
        // Single-instance guard
        using var mutex = new Mutex(true, "AppleMusicRPC_SingleInstance", out bool isNew);
        if (!isNew) return;

        using var config  = new ConfigService();
        using var tray    = new TrayManager(config);
        using var discord = new DiscordIpcClient();
        var albumArt      = new AlbumArtService();
        using var smtc    = new SmtcWatcher();
        using var cts     = new CancellationTokenSource();

        MediaInfo? lastInfo = null;
        bool rpcEnabled = true;

        Log.Info("=== Apple Music RPC démarrage ===");
        config.Load();
        Log.Info("Config chargée");

        discord.OnReady += () =>
        {
            Log.Info("Discord READY — envoi activité si lastInfo disponible");
            tray.ShowBalloon("Apple Music RPC", "Connecté à Discord ✓");
            if (lastInfo is not null)
                _ = UpdateDiscordAsync(lastInfo);
        };

        discord.OnDisconnected += () => Log.Warn("Discord déconnecté");

        smtc.MediaChanged += async (_, info) =>
        {
            Log.Info($"SMTC event: [{info.Status}] {info.Artist} - {info.Title} | rpcEnabled={rpcEnabled} | discordReady={discord.IsReady}");
            lastInfo = info;
            tray.SetTrack(info);
            if (!rpcEnabled) return;
            await UpdateDiscordAsync(info);
        };

        tray.ToggleRpcRequested += () =>
        {
            rpcEnabled = !rpcEnabled;
            if (!rpcEnabled) _ = discord.ClearActivityAsync();
            else if (lastInfo is not null) _ = UpdateDiscordAsync(lastInfo);
        };

        tray.CheckUpdateRequested += () =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/Kendral444/apple-music-rpc/releases/latest",
                UseShellExecute = true
            });

        tray.UninstallRequested += () =>
        {
            var updaterPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AppleMusicRPC", "Apple Music RPC Updater.exe");

            if (File.Exists(updaterPath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = updaterPath,
                    Arguments       = "--uninstall",
                    UseShellExecute = true
                });
            }
            cts.Cancel();
        };

        tray.QuitRequested += () => cts.Cancel();

        _ = discord.RunAsync(cts.Token);
        _ = smtc.StartAsync(cts.Token);
        Log.Info("Tâches Discord + SMTC lancées, démarrage boucle tray...");

        // Boucle de messages Win32 (bloquant jusqu'au Quit)
        tray.Run();
        Log.Info("Boucle tray terminée.");

        async Task UpdateDiscordAsync(MediaInfo info)
        {
            var cfg = config.Current;

            if (info.Status == PlaybackStatus.Stopped
             || (info.Status == PlaybackStatus.Paused && cfg.Behavior.HideWhenPaused))
            {
                Log.Info($"UpdateDiscord: clear (status={info.Status})");
                await discord.ClearActivityAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(info.Title))
            {
                Log.Warn("UpdateDiscord: titre vide, abandon");
                return;
            }

            try
            {
                Log.Info($"UpdateDiscord: résolution pochette pour [{info.Artist} - {info.Title}]");
                var (art, url) = await albumArt.ResolveAsync(info.Artist, info.Album, info.Title);
                Log.Info($"UpdateDiscord: art={art ?? "null"} | ready={discord.IsReady}");
                await discord.SetActivityAsync(info, art, url, cfg);
                Log.Info("UpdateDiscord: SetActivity envoyé");
            }
            catch (Exception ex)
            {
                Log.Error("UpdateDiscord exception", ex);
            }
        }
    }
}
