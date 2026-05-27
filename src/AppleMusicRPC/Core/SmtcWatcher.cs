using AppleMusicRPC.Models;
using Windows.Media.Control;

namespace AppleMusicRPC.Core;

public class SmtcWatcher : IDisposable
{
    public event AsyncEventHandler<MediaInfo>? MediaChanged;

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private CancellationTokenSource _debounceCts = new();
    private readonly object _lock = new();

    public async Task StartAsync(CancellationToken ct = default)
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        await AttachSessionAsync(_manager.GetCurrentSession());

        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
    }

    private async void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        await AttachSessionAsync(sender.GetCurrentSession());
    }

    private async Task AttachSessionAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        lock (_lock)
        {
            if (_session is not null)
            {
                _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
            }
            _session = session;
        }

        if (session is null)
        {
            await RaiseStoppedAsync();
            return;
        }

        // Whitelist stricte : Apple Music et iTunes uniquement
        var source = session.SourceAppUserModelId ?? "";
        if (!source.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)
         && !source.Contains("iTunes", StringComparison.OrdinalIgnoreCase))
        {
            await RaiseStoppedAsync();
            return;
        }

        session.MediaPropertiesChanged += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        await DispatchUpdateAsync();
    }

    private async void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => await DispatchUpdateAsync();

    private async void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => await DispatchUpdateAsync();

    private async void OnTimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args) => await DispatchUpdateAsync(debounceMs: 800);

    private async Task DispatchUpdateAsync(int debounceMs = 120)
    {
        CancellationToken token;
        lock (_lock)
        {
            _debounceCts.Cancel();
            _debounceCts = new CancellationTokenSource();
            token = _debounceCts.Token;
        }

        try
        {
            await Task.Delay(debounceMs, token);

            GlobalSystemMediaTransportControlsSession? session;
            lock (_lock) session = _session;
            if (session is null) return;

            var media = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            if (media is null || playback is null) return;

            var title = media.Title ?? "";
            var artist = media.Artist ?? "";
            var album = media.AlbumTitle ?? "";

            if (string.IsNullOrWhiteSpace(title)) return;

            var status = playback.PlaybackStatus switch
            {
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => PlaybackStatus.Playing,
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused  => PlaybackStatus.Paused,
                _                                                               => PlaybackStatus.Stopped
            };

            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long posMs = (long)(timeline?.Position.TotalMilliseconds ?? 0);
            long endMs = (long)(timeline?.EndTime.TotalMilliseconds ?? 0);

            var info = new MediaInfo(
                title, artist, album, status,
                StartTimestampMs: nowMs - posMs,
                EndTimestampMs:   nowMs + (endMs - posMs)
            );

            if (MediaChanged is not null)
                await MediaChanged.Invoke(this, info);
        }
        catch (OperationCanceledException) { }
        catch { /* session fermée entre temps */ }
    }

    private async Task RaiseStoppedAsync()
    {
        var stopped = new MediaInfo("", "", "", PlaybackStatus.Stopped, 0, 0);
        if (MediaChanged is not null)
            await MediaChanged.Invoke(this, stopped);
    }

    public void Dispose()
    {
        _debounceCts.Cancel();
        _debounceCts.Dispose();
        if (_manager is not null)
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
    }
}

public delegate Task AsyncEventHandler<TArgs>(object sender, TArgs args);
