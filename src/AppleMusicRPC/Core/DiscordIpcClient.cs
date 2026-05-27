using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppleMusicRPC.Models;

namespace AppleMusicRPC.Core;

public class DiscordIpcClient : IDisposable
{
    private const string ClientId = "1114806909590048798";

    private NamedPipeClientStream? _pipe;
    private bool _ready;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private CancellationTokenSource _cts = new();

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
            catch { /* Discord fermé */ }
            finally
            {
                _ready = false;
                _pipe?.Dispose();
                _pipe = null;
                OnDisconnected?.Invoke();
            }

            if (!ct.IsCancellationRequested)
                await Task.Delay(10_000, ct);
        }
    }

    private async Task ConnectAsync(CancellationToken ct)
    {
        for (int i = 0; i < 10; i++)
        {
            try
            {
                var pipe = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(500, ct);
                _pipe = pipe;
                break;
            }
            catch { /* essai suivant */ }
        }

        if (_pipe is null) throw new Exception("Discord introuvable");

        // Handshake
        await SendFrameAsync(0, $"{{\"v\":1,\"client_id\":\"{ClientId}\"}}");
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var header = new byte[8];
        while (_pipe?.IsConnected == true && !ct.IsCancellationRequested)
        {
            await ReadExactlyAsync(header, ct);
            int opcode = BitConverter.ToInt32(header, 0);
            int length  = BitConverter.ToInt32(header, 4);

            var payload = new byte[length];
            await ReadExactlyAsync(payload, ct);

            if (opcode == 1) HandleFrame(Encoding.UTF8.GetString(payload));
            else if (opcode == 2) break; // CLOSE
        }
    }

    private void HandleFrame(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("evt", out var evt)
                && evt.GetString() == "READY")
            {
                _ready = true;
                OnReady?.Invoke();
            }
        }
        catch { }
    }

    private async Task ReadExactlyAsync(byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await _pipe!.ReadAsync(buffer.AsMemory(read), ct);
            if (n == 0) throw new EndOfStreamException("Pipe fermé");
            read += n;
        }
    }

    public async Task SetActivityAsync(MediaInfo info, string? artUrl, string trackUrl, AppConfig config)
    {
        if (!_ready) return;

        var activity = BuildActivity(info, artUrl, trackUrl, config);
        var payload = new
        {
            cmd = "SET_ACTIVITY",
            args = new { pid = Environment.ProcessId, activity },
            nonce = Guid.NewGuid().ToString("N")
        };
        await SendFrameAsync(1, JsonSerializer.Serialize(payload, JsonCtx.Default.Object));
    }

    public async Task ClearActivityAsync()
    {
        if (!_ready) return;
        var payload = new
        {
            cmd = "SET_ACTIVITY",
            args = new { pid = Environment.ProcessId, activity = (object?)null },
            nonce = Guid.NewGuid().ToString("N")
        };
        await SendFrameAsync(1, JsonSerializer.Serialize(payload, JsonCtx.Default.Object));
    }

    private static object BuildActivity(MediaInfo info, string? artUrl, string trackUrl, AppConfig cfg)
    {
        var display = cfg.Display;

        var details = SanitizeForDiscord(info.Title);
        var state   = display.ShowArtist ? SanitizeForDiscord(info.Artist) : null;
        var largeText = display.ShowAlbum && !string.IsNullOrWhiteSpace(info.Album)
            ? SanitizeForDiscord(info.Album)
            : "Apple Music";

        object? timestamps = null;
        if (display.ShowTimestamps && info.Status == PlaybackStatus.Playing
            && info.StartTimestampMs > 0 && info.EndTimestampMs > info.StartTimestampMs)
        {
            timestamps = new { start = info.StartTimestampMs, end = info.EndTimestampMs };
        }

        object? assets = display.ShowAlbumArt
            ? new { large_image = artUrl ?? "apple_music_logo", large_text = largeText }
            : new { large_image = "apple_music_logo", large_text = "Apple Music" };

        object[]? buttons = display.ShowButton && !string.IsNullOrWhiteSpace(display.ButtonLabel)
            ? [new { label = display.ButtonLabel, url = trackUrl }]
            : null;

        return new { details, state, assets, timestamps, buttons, type = 2, instance = false };
    }

    private static string SanitizeForDiscord(string s) =>
        (s ?? "").Replace("\x00", "").Trim() is { Length: > 0 } clean ? clean : "​";

    private async Task SendFrameAsync(int opcode, string json)
    {
        var payload = Encoding.UTF8.GetBytes(json);
        var header  = new byte[8];
        BitConverter.TryWriteBytes(header.AsSpan(0), opcode);
        BitConverter.TryWriteBytes(header.AsSpan(4), payload.Length);

        await _writeLock.WaitAsync();
        try
        {
            await _pipe!.WriteAsync(header);
            await _pipe!.WriteAsync(payload);
            await _pipe!.FlushAsync();
        }
        finally { _writeLock.Release(); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _pipe?.Dispose();
        _writeLock.Dispose();
    }
}

// Contexte de sérialisation source-generated pour éviter la réflexion à l'exécution
[JsonSerializable(typeof(object))]
internal partial class JsonCtx : JsonSerializerContext { }
