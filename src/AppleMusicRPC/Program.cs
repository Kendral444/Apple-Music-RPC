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
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

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

        config.Load();

        // Discord RPC : reconnexion automatique en arrière-plan
        discord.OnReady += () =>
        {
            tray.ShowBalloon("Apple Music RPC", "Connecté à Discord ✓");
            if (lastInfo is not null)
                _ = UpdateDiscordAsync(lastInfo);
        };

        // SMTC → Discord
        smtc.MediaChanged += async (_, info) =>
        {
            lastInfo = info;
            tray.SetTrack(info);

            if (!rpcEnabled) return;
            await UpdateDiscordAsync(info);
        };

        tray.ToggleRpcRequested += () =>
        {
            rpcEnabled = !rpcEnabled;
            if (!rpcEnabled)
                _ = discord.ClearActivityAsync();
            else if (lastInfo is not null)
                _ = UpdateDiscordAsync(lastInfo);
        };

        tray.CheckUpdateRequested += () =>
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://github.com/Kendral444/apple-music-rpc/releases/latest",
                UseShellExecute = true
            });

        // Démarrage des boucles async
        _ = discord.RunAsync(cts.Token);
        _ = smtc.StartAsync(cts.Token);

        Application.ApplicationExit += (_, _) => cts.Cancel();
        Application.Run(tray.Context);

        async Task UpdateDiscordAsync(MediaInfo info)
        {
            var cfg = config.Current;

            if (info.Status == PlaybackStatus.Stopped
             || (info.Status == PlaybackStatus.Paused && cfg.Behavior.HideWhenPaused))
            {
                await discord.ClearActivityAsync();
                return;
            }

            if (string.IsNullOrWhiteSpace(info.Title)) return;

            var (art, url) = await albumArt.ResolveAsync(info.Artist, info.Album, info.Title);
            await discord.SetActivityAsync(info, art, url, cfg);
        }
    }
}
