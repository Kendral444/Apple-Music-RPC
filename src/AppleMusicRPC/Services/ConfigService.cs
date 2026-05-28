using System.Text.Json;
using System.Text.Json.Serialization;
using AppleMusicRPC.Models;

namespace AppleMusicRPC.Services;

public class ConfigService : IDisposable
{
    public static readonly string AppDataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppleMusicRPC"
    );

    private static readonly string ConfigPath = Path.Combine(AppDataDir, "config.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private AppConfig _config = new();
    private readonly object _lock = new();
    private FileSystemWatcher? _watcher;

    public event Action<AppConfig>? ConfigChanged;

    public AppConfig Current
    {
        get { lock (_lock) return _config; }
    }

    public void Load()
    {
        Directory.CreateDirectory(AppDataDir);
        if (!File.Exists(ConfigPath)) Save(new AppConfig());
        Reload();
        StartWatcher();
    }

    private void Reload()
    {
        try
        {
            var json   = File.ReadAllText(ConfigPath);
            var loaded = JsonSerializer.Deserialize(json, ConfigJsonCtx.Default.AppConfig);
            if (loaded is not null)
            {
                lock (_lock) _config = loaded;
                ConfigChanged?.Invoke(loaded);
            }
        }
        catch { /* Config malformée : on garde l'ancienne */ }
    }

    public void Save(AppConfig config)
    {
        lock (_lock) _config = config;
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, ConfigJsonCtx.Default.AppConfig));
    }

    public void OpenInEditor()
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = ConfigPath,
            UseShellExecute = true
        });
    }

    private void StartWatcher()
    {
        _watcher = new FileSystemWatcher(AppDataDir, "config.json")
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true
        };
        _watcher.Changed += (_, _) => { Thread.Sleep(200); Reload(); };
    }

    public void Dispose() => _watcher?.Dispose();
}

// ── JSON source-generated context ─────────────────────────────────────────────
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(DisplayConfig))]
[JsonSerializable(typeof(BehaviorConfig))]
internal partial class ConfigJsonCtx : JsonSerializerContext { }
