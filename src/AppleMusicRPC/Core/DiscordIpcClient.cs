using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppleMusicRPC.Models;

namespace AppleMusicRPC.Core;

// ── DTOs Discord (sérialisation source-generated, trim-safe) ──────────────────

internal sealed class RpcPayload
{
    [JsonPropertyName("cmd")]   public string    Cmd   { get; init; } = "";
    [JsonPropertyName("args")]  public RpcArgs   Args  { get; init; } = new();
    [JsonPropertyName("nonce")] public string    Nonce { get; init; } = "";
}

internal sealed class RpcArgs
{
    [JsonPropertyName("pid")]      public int             Pid      { get; init; }
    [JsonPropertyName("activity")] public RpcActivity?   Activity { get; init; }
}

internal sealed class RpcActivity
{
    [JsonPropertyName("details")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Details { get; init; }

    [JsonPropertyName("state")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? State { get; init; }

    [JsonPropertyName("assets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RpcAssets? Assets { get; init; }

    [JsonPropertyName("timestamps")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RpcTimestamps? Timestamps { get; init; }

    [JsonPropertyName("buttons")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RpcButton[]? Buttons { get; init; }

    [JsonPropertyName("type")]
    public int Type { get; init; }

    [JsonPropertyName("instance")]
    public bool Instance { get; init; }
}

internal sealed class RpcAssets
{
    [JsonPropertyName("large_image")] public string? LargeImage { get; init; }
    [JsonPropertyName("large_text")]  public string? LargeText  { get; init; }
}

internal sealed class RpcTimestamps
{
    [JsonPropertyName("start")] public long Start { get; init; }
    [JsonPropertyName("end")]   public long End   { get; init; }
}

internal sealed class RpcButton
{
    [JsonPropertyName("label")] public string Label { get; init; } = "";
    [JsonPropertyName("url")]   public string Url   { get; init; } = "";
}

[JsonSerializable(typeof(RpcPayload))]
internal partial class RpcJsonCtx : JsonSerializerContext { }

// ── Client IPC ────────────────────────────────────────────────────────────────

public class DiscordIpcClient : IDisposable
{
    private const string ClientId = "1114806909590048798";

    private NamedPipeClientStream? _pipe;
    private bool _ready;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public bool IsReady => _ready;
    public event Action? OnReady;
    public event Action? OnDisconnected;

    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAsync(ct);
                await ReadLoopAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch { /* Discord fermé / injoignable */ }
            finally
            {
                _ready = false;
                _pipe?.Dispose();
                _pipe = null;
                OnDisconnected?.Invoke();
            }

            if (!ct.IsCancellationRequested)
                await Task.Delay(10_000, ct).ConfigureAwait(false);
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        Log.Info("DiscordIPC: tentative connexion...");
        for (int i = 0; i < 10; i++)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}",
                    PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(500, ct);
                _pipe = pipe;
                Log.Info($"DiscordIPC: connecté sur discord-ipc-{i}");
                break;
            }
            catch { /* essai pipe suivant */ }
        }

        if (_pipe is null) throw new InvalidOperationException("Discord introuvable sur les pipes 0-9.");

        await SendRawAsync(0, $"{{\"v\":1,\"client_id\":\"{ClientId}\"}}");
        Log.Info("DiscordIPC: handshake envoyé");
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var header = new byte[8];
        while (_pipe?.IsConnected == true && !ct.IsCancellationRequested)
        {
            await ReadExactlyAsync(header, ct);

            int opcode = BitConverter.ToInt32(header, 0);
            int length  = BitConverter.ToInt32(header, 4);

            var body = new byte[Math.Max(0, length)];
            if (length > 0) await ReadExactlyAsync(body, ct);

            switch (opcode)
            {
                case 1: HandleFrame(Encoding.UTF8.GetString(body)); break;
                case 2: return; // CLOSE envoyé par Discord
            }
        }
    }

    private void HandleFrame(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("evt", out var evt))
            {
                var evtStr = evt.GetString();
                if (evtStr == "READY")
                {
                    _ready = true;
                    Log.Info("DiscordIPC: READY ✓");
                    OnReady?.Invoke();
                }
                else if (evtStr == "ERROR")
                {
                    // Logger toute la réponse pour diagnostiquer
                    Log.Error($"DiscordIPC: ERROR reçu → {json}");
                }
                else
                {
                    Log.Info($"DiscordIPC: frame evt={evtStr ?? "null"}");
                }
            }
            else
            {
                // Réponse sans evt (ex: réponse à SET_ACTIVITY)
                var cmd = doc.RootElement.TryGetProperty("cmd", out var c) ? c.GetString() : "?";
                Log.Info($"DiscordIPC: réponse cmd={cmd} → {json[..Math.Min(300, json.Length)]}");
            }
        }
        catch (Exception ex) { Log.Error("DiscordIPC HandleFrame", ex); }
    }

    private async Task ReadExactlyAsync(byte[] buf, CancellationToken ct)
    {
        int read = 0;
        while (read < buf.Length)
        {
            int n = await _pipe!.ReadAsync(buf.AsMemory(read), ct);
            if (n == 0) throw new EndOfStreamException("Pipe Discord fermé.");
            read += n;
        }
    }

    // ── API publique ──────────────────────────────────────────────────────────

    public Task SetActivityAsync(MediaInfo info, string? artUrl, string trackUrl, AppConfig config)
    {
        if (!_ready || _pipe is null) return Task.CompletedTask;

        var cfg     = config.Display;
        var details = Sanitize(info.Title);
        var state   = cfg.ShowArtist ? Sanitize(info.Artist) : null;

        RpcTimestamps? timestamps = null;
        if (cfg.ShowTimestamps && info.Status == PlaybackStatus.Playing
            && info.StartTimestampMs > 0 && info.EndTimestampMs > info.StartTimestampMs)
        {
            timestamps = new RpcTimestamps
            {
                Start = info.StartTimestampMs,
                End   = info.EndTimestampMs
            };
        }

        var largeText = cfg.ShowAlbum && !string.IsNullOrWhiteSpace(info.Album)
            ? Sanitize(info.Album)
            : "Apple Music";

        var assets = new RpcAssets
        {
            LargeImage = cfg.ShowAlbumArt ? (artUrl ?? "apple_music_logo") : "apple_music_logo",
            LargeText  = largeText
        };

        RpcButton[]? buttons = cfg.ShowButton && !string.IsNullOrWhiteSpace(cfg.ButtonLabel)
            ? [new RpcButton { Label = cfg.ButtonLabel, Url = trackUrl }]
            : null;

        var activity = new RpcActivity
        {
            Details    = details,
            State      = state,
            Assets     = assets,
            Timestamps = timestamps,
            Buttons    = buttons,
            Type       = 2,       // Listening
            Instance   = false
        };

        return SendPayloadAsync(activity);
    }

    public Task ClearActivityAsync()
    {
        if (!_ready || _pipe is null) return Task.CompletedTask;
        return SendPayloadAsync(null);
    }

    private Task SendPayloadAsync(RpcActivity? activity)
    {
        var payload = new RpcPayload
        {
            Cmd   = "SET_ACTIVITY",
            Args  = new RpcArgs { Pid = Environment.ProcessId, Activity = activity },
            Nonce = Guid.NewGuid().ToString("N")
        };

        var json = JsonSerializer.Serialize(payload, RpcJsonCtx.Default.RpcPayload);
        Log.Info($"DiscordIPC: SendPayload → {json[..Math.Min(200, json.Length)]}");
        return SendRawAsync(1, json);
    }

    private async Task SendRawAsync(int opcode, string json)
    {
        var body   = Encoding.UTF8.GetBytes(json);
        var header = new byte[8];
        BitConverter.TryWriteBytes(header.AsSpan(0), opcode);
        BitConverter.TryWriteBytes(header.AsSpan(4), body.Length);

        await _writeLock.WaitAsync();
        try
        {
            await _pipe!.WriteAsync(header);
            await _pipe!.WriteAsync(body);
            await _pipe!.FlushAsync();
        }
        finally { _writeLock.Release(); }
    }

    private static string Sanitize(string? s) =>
        (s ?? "").Replace("\x00", "").Trim() is { Length: > 0 } v ? v : "​";

    public void Dispose()
    {
        _pipe?.Dispose();
        _writeLock.Dispose();
    }
}
