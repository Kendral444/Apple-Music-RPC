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
        Log.Info("SmtcWatcher: RequestAsync...");
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        Log.Info("SmtcWatcher: SessionManager obtenu");

        // SessionsChanged = fire quand n'importe quelle app commence/arrête de jouer
        // CurrentSessionChanged = fire quand l'app "active" change (ex: Chrome prend le focus)
        // On veut LES DEUX pour scanner toutes les sessions à chaque changement.
        _manager.SessionsChanged += OnSessionsChanged;
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;

        // Scan initial de toutes les sessions existantes
        await ScanAllSessionsAsync(_manager.GetSessions());

        await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
    }

    // ── Handlers session manager ─────────────────────────────────────────────

    private async void OnSessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
    {
        Log.Info("SmtcWatcher: SessionsChanged — re-scan...");
        await ScanAllSessionsAsync(sender.GetSessions());
    }

    private async void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        // Même si Chrome devient "current", Apple Music joue peut-être encore.
        // On rescanne toutes les sessions plutôt que de prendre GetCurrentSession().
        Log.Info("SmtcWatcher: CurrentSessionChanged — re-scan...");
        await ScanAllSessionsAsync(sender.GetSessions());
    }

    /// <summary>
    /// Parcourt TOUTES les sessions actives et s'attache à la première
    /// qui provient d'Apple Music ou iTunes.
    /// </summary>
    private async Task ScanAllSessionsAsync(
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        GlobalSystemMediaTransportControlsSession? target = null;

        foreach (var s in sessions)
        {
            var src = s.SourceAppUserModelId ?? "";
            Log.Info($"SmtcWatcher: session trouvée = {src}");

            if (src.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("iTunes",     StringComparison.OrdinalIgnoreCase))
            {
                target = s;
                break;
            }
        }

        if (target is null)
            Log.Warn($"SmtcWatcher: Apple Music introuvable parmi {sessions.Count} session(s)");

        await AttachSessionAsync(target);
    }

    // ── Attachement session Apple Music ──────────────────────────────────────

    private async Task AttachSessionAsync(GlobalSystemMediaTransportControlsSession? session)
    {
        lock (_lock)
        {
            // Désabonnement de l'ancienne session si elle a changé
            if (_session is not null && !ReferenceEquals(_session, session))
            {
                _session.MediaPropertiesChanged  -= OnMediaPropertiesChanged;
                _session.PlaybackInfoChanged     -= OnPlaybackInfoChanged;
                _session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
                Log.Info("SmtcWatcher: désabonnement ancienne session");
            }
            _session = session;
        }

        if (session is null)
        {
            await RaiseStoppedAsync();
            return;
        }

        Log.Info($"SmtcWatcher: attachement à {session.SourceAppUserModelId}");
        session.MediaPropertiesChanged  += OnMediaPropertiesChanged;
        session.PlaybackInfoChanged     += OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

        await DispatchUpdateAsync();
    }

    // ── Handlers session Apple Music ─────────────────────────────────────────

    private async void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession s,
        MediaPropertiesChangedEventArgs _) => await DispatchUpdateAsync();

    private async void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession s,
        PlaybackInfoChangedEventArgs _) => await DispatchUpdateAsync();

    private async void OnTimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession s,
        TimelinePropertiesChangedEventArgs _) => await DispatchUpdateAsync(debounceMs: 800);

    // ── Dispatch ─────────────────────────────────────────────────────────────

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

            var media    = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            if (media is null || playback is null) return;

            var title  = media.Title       ?? "";
            var artist = media.Artist      ?? "";
            var album  = media.AlbumTitle  ?? "";

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
        catch (Exception ex) { Log.Error("SmtcWatcher DispatchUpdateAsync", ex); }
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
        {
            _manager.SessionsChanged       -= OnSessionsChanged;
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
    }
}

public delegate Task AsyncEventHandler<TArgs>(object sender, TArgs args);
