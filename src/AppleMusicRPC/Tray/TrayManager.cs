using AppleMusicRPC.Models;
using AppleMusicRPC.Services;

namespace AppleMusicRPC.Tray;

public class TrayManager : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly ConfigService _configService;
    private bool _rpcEnabled = true;

    public ApplicationContext Context { get; }
    public event Action? ToggleRpcRequested;
    public event Action? CheckUpdateRequested;

    public TrayManager(ConfigService configService)
    {
        _configService = configService;

        _icon = new NotifyIcon
        {
            Icon      = LoadIcon(),
            Visible   = true,
            Text      = "Apple Music RPC",
            ContextMenuStrip = BuildMenu()
        };

        Context = new ApplicationContext();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var header = new ToolStripMenuItem("Apple Music RPC") { Enabled = false };
        header.Font = new Font(header.Font, FontStyle.Bold);

        var toggleItem = new ToolStripMenuItem("✓  RPC activé");
        toggleItem.Click += (_, _) =>
        {
            _rpcEnabled = !_rpcEnabled;
            toggleItem.Text = _rpcEnabled ? "✓  RPC activé" : "    RPC désactivé";
            ToggleRpcRequested?.Invoke();
        };

        var configItem = new ToolStripMenuItem("Ouvrir la configuration");
        configItem.Click += (_, _) => _configService.OpenInEditor();

        var updateItem = new ToolStripMenuItem("Vérifier les mises à jour");
        updateItem.Click += (_, _) => CheckUpdateRequested?.Invoke();

        var quitItem = new ToolStripMenuItem("Quitter");
        quitItem.Click += (_, _) =>
        {
            _icon.Visible = false;
            Application.Exit();
        };

        menu.Items.AddRange([
            header,
            new ToolStripSeparator(),
            toggleItem,
            new ToolStripSeparator(),
            configItem,
            updateItem,
            new ToolStripSeparator(),
            quitItem
        ]);

        return menu;
    }

    public void SetTrack(MediaInfo? info)
    {
        var text = info is { Status: PlaybackStatus.Playing or PlaybackStatus.Paused }
            ? $"{info.Title} — {info.Artist}"
            : "Apple Music RPC";

        // NotifyIcon.Text limite à 63 caractères
        _icon.Text = text.Length > 63 ? text[..60] + "..." : text;
    }

    public void ShowBalloon(string title, string message)
    {
        _icon.ShowBalloonTip(3000, title, message, ToolTipIcon.None);
    }

    private static Icon LoadIcon()
    {
        var stream = typeof(TrayManager).Assembly.GetManifestResourceStream("appicon.ico");
        return stream is not null ? new Icon(stream) : SystemIcons.Application;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
