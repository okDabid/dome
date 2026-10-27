using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Dome.Core;

namespace Dome.Services;

public record LyricsResult(IReadOnlyList<LyricLine> Lines, string? Plain, string Message);

public sealed class LyricsService : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(12) };
    private readonly Dictionary<string, LyricsResult> _cache = new();
    public LyricsService() => _http.DefaultRequestHeaders.UserAgent.ParseAdd("Dome/1.0 (Windows desktop lyrics)");
    public async Task<LyricsResult> LookupAsync(TrackInfo track, TimeSpan duration, CancellationToken cancellation)
    {
        if (string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
            return new([], null, "This player hasn't shared a title and artist.");
        var key = $"{track.Title}\n{track.Artist}\n{track.Album}\n{duration.TotalSeconds:0}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        var url = "https://lrclib.net/api/get?track_name=" + Uri.EscapeDataString(track.Title)
            + "&artist_name=" + Uri.EscapeDataString(track.Artist);
        if (!string.IsNullOrWhiteSpace(track.Album)) url += "&album_name=" + Uri.EscapeDataString(track.Album);
        if (duration > TimeSpan.Zero) url += "&duration=" + Math.Round(duration.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var response = await _http.GetAsync(url, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound) return new([], null, "No lyrics found for this recording.");
        response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<LyricsResponse>(cancellationToken: cancellation);
        var lines = Lyrics.Parse(data?.Synced);
        var result = data?.Instrumental == true ? new LyricsResult([], null, "Instrumental. Enjoy the moment.")
            : new LyricsResult(lines, data?.Plain, lines.Count > 0 ? "Synced lyrics · LRCLIB" : "Unsynced lyrics · LRCLIB");
        cancellation.ThrowIfCancellationRequested();
        if (_cache.Count >= 12) _cache.Clear();
        _cache[key] = result;
        return result;
    }
    public void Dispose() => _http.Dispose();
    private sealed class LyricsResponse
    {
        [JsonPropertyName("syncedLyrics")] public string? Synced { get; set; }
        [JsonPropertyName("plainLyrics")] public string? Plain { get; set; }
        [JsonPropertyName("instrumental")] public bool Instrumental { get; set; }
    }
}
