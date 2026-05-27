namespace AppleMusicRPC.Models;

public enum PlaybackStatus { Playing, Paused, Stopped }

public record MediaInfo(
    string Title,
    string Artist,
    string Album,
    PlaybackStatus Status,
    long StartTimestampMs,
    long EndTimestampMs
);
