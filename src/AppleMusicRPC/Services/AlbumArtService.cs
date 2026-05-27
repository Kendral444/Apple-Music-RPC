using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AppleMusicRPC.Services;

public class AlbumArtService
{
    private record TrackResult(string? Art, string Url);

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, TrackResult> _cache = new(300);
    private readonly object _cacheLock = new();

    public async Task<(string? art, string url)> ResolveAsync(string artist, string album, string title)
    {
        var mainArtist = ExtractMainArtist(artist);
        var cleanTitle = SanitizeSearchTerm(title);
        var cacheKey = $"{mainArtist}|{cleanTitle}";

        lock (_cacheLock)
        {
            if (_cache.TryGetValue(cacheKey, out var cached))
                return (cached.Art, cached.Url);
        }

        var result = await TryItunesAsync(mainArtist, cleanTitle)
                  ?? await TryDeezerAsync(mainArtist, cleanTitle)
                  ?? BuildAvatarFallback(mainArtist, cleanTitle);

        lock (_cacheLock)
        {
            if (_cache.Count >= 300)
            {
                var first = _cache.Keys.First();
                _cache.Remove(first);
            }
            _cache[cacheKey] = result;
        }

        return (result.Art, result.Url);
    }

    private async Task<TrackResult?> TryItunesAsync(string artist, string title)
    {
        try
        {
            var query = Uri.EscapeDataString($"{artist} {title}");
            var url = $"https://itunes.apple.com/search?term={query}&media=music&entity=song&limit=5&country=FR";

            var response = await _http.GetFromJsonAsync<ItunesResponse>(url);
            if (response?.Results is null || response.Results.Length == 0) return null;

            var match = response.Results.FirstOrDefault(r =>
            {
                var apiArtist = (r.ArtistName ?? "").ToLowerInvariant();
                var apiTrack = (r.TrackName ?? "").ToLowerInvariant();
                var qArtist = artist.ToLowerInvariant();
                var qTrack = title.ToLowerInvariant();
                return (apiArtist.Contains(qArtist) || qArtist.Contains(apiArtist))
                    && (apiTrack.Contains(qTrack) || qTrack.Contains(apiTrack));
            });

            if (match is null) return null;

            var artUrl = match.ArtworkUrl100?.Replace("100x100bb", "512x512bb");
            return new TrackResult(artUrl, match.TrackViewUrl ?? "https://music.apple.com/");
        }
        catch { return null; }
    }

    private async Task<TrackResult?> TryDeezerAsync(string artist, string title)
    {
        try
        {
            var query = Uri.EscapeDataString($"artist:\"{artist}\" track:\"{title}\"");
            var url = $"https://api.deezer.com/search?q={query}&limit=1";

            var response = await _http.GetFromJsonAsync<DeezerResponse>(url);
            var item = response?.Data?.FirstOrDefault();
            if (item is null) return null;

            return new TrackResult(item.Album?.CoverXl, item.Link ?? "https://music.apple.com/");
        }
        catch { return null; }
    }

    private static TrackResult BuildAvatarFallback(string artist, string title)
    {
        var initials = $"{(title.Length > 0 ? title[0] : '?')}{(artist.Length > 0 ? artist[0] : '?')}".ToUpperInvariant();
        var art = $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(initials)}&background=fc3c44&color=fff&size=512&bold=true";
        return new TrackResult(art, "https://music.apple.com/");
    }

    private static string SanitizeSearchTerm(string term) =>
        Regex.Replace(term, @"\s*[-–]\s*Single$|\s*[-–]\s*EP$|\s*\(feat\..*?\)|\[.*?\]|\s*feat\.\s.*|\s*ft\.\s.*",
            "", RegexOptions.IgnoreCase).Trim();

    private static string ExtractMainArtist(string artist) =>
        Regex.Split(artist, @"\s[—\-,&]\s|feat\.|ft\.")[0].Trim();

    // --- iTunes models ---
    private record ItunesResponse(
        [property: JsonPropertyName("resultCount")] int ResultCount,
        [property: JsonPropertyName("results")] ItunesTrack[]? Results);

    private record ItunesTrack(
        [property: JsonPropertyName("artistName")] string? ArtistName,
        [property: JsonPropertyName("trackName")] string? TrackName,
        [property: JsonPropertyName("artworkUrl100")] string? ArtworkUrl100,
        [property: JsonPropertyName("trackViewUrl")] string? TrackViewUrl);

    // --- Deezer models ---
    private record DeezerResponse(
        [property: JsonPropertyName("data")] DeezerTrack[]? Data);

    private record DeezerTrack(
        [property: JsonPropertyName("link")] string? Link,
        [property: JsonPropertyName("album")] DeezerAlbum? Album);

    private record DeezerAlbum(
        [property: JsonPropertyName("cover_xl")] string? CoverXl);
}
