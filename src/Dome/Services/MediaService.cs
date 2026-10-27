using System;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Dome.Services;

public record TrackInfo(string Title, string Artist, string Album, string Player, IRandomAccessStreamReference? Thumbnail);

public sealed class MediaService : IDisposable
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    public GlobalSystemMediaTransportControlsSession? Session { get; private set; }
    public event Action? TrackChanged;
    public event Action? PlaybackChanged;

    public async Task StartAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += CurrentChanged;
        _manager.SessionsChanged += SessionsChanged;
        Bind();
    }
    private void CurrentChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => Bind();
    private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => Bind();
    private void Bind()
    {
        var next = _manager?.GetCurrentSession();
        if (Equals(Session, next)) return;
        Unbind();
        Session = next;
        if (Session != null)
        {
            Session.MediaPropertiesChanged += PropertiesChanged;
            Session.PlaybackInfoChanged += InfoChanged;
            Session.TimelinePropertiesChanged += TimelineChanged;
        }
        TrackChanged?.Invoke();
    }
    private void PropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => TrackChanged?.Invoke();
    private void InfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => PlaybackChanged?.Invoke();
    private void TimelineChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args) => PlaybackChanged?.Invoke();
    public async Task<TrackInfo?> ReadTrackAsync()
    {
        var session = Session;
        if (session == null) return null;
        var properties = await session.TryGetMediaPropertiesAsync();
        return new(properties.Title, properties.Artist, properties.AlbumTitle, session.SourceAppUserModelId, properties.Thumbnail);
    }
    public (TimeSpan Position, TimeSpan Start, TimeSpan End, bool Playing, bool CanSeek, bool CanPlay, bool CanNext, bool CanPrevious) ReadPlayback()
    {
        if (Session == null) return default;
        var playback = Session.GetPlaybackInfo();
        var timeline = Session.GetTimelineProperties();
        var playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        var position = timeline.Position;
        if (playing)
        {
            var elapsed = DateTimeOffset.UtcNow - timeline.LastUpdatedTime;
            // Some players do not expose a valid timestamp.
            if (timeline.LastUpdatedTime.Year > 2000 && elapsed >= TimeSpan.Zero)
                position += TimeSpan.FromTicks((long)(elapsed.Ticks * (playback.PlaybackRate ?? 1)));
        }
        if (position < timeline.StartTime) position = timeline.StartTime;
        if (timeline.EndTime > timeline.StartTime && position > timeline.EndTime) position = timeline.EndTime;
        var controls = playback.Controls;
        return (position, timeline.StartTime, timeline.EndTime, playing,
            controls.IsPlaybackPositionEnabled, controls.IsPlayPauseToggleEnabled,
            controls.IsNextEnabled, controls.IsPreviousEnabled);
    }
    public async Task<bool> TransportAsync(string action)
    {
        var session = Session;
        if (session == null) return false;
        return action switch
        {
            "previous" => await session.TrySkipPreviousAsync(),
            "next" => await session.TrySkipNextAsync(),
            _ => await session.TryTogglePlayPauseAsync()
        };
    }
    public async Task<bool> SeekAsync(TimeSpan position)
    {
        var session = Session;
        return session != null && await session.TryChangePlaybackPositionAsync(position.Ticks);
    }
    private void Unbind()
    {
        if (Session == null) return;
        Session.MediaPropertiesChanged -= PropertiesChanged;
        Session.PlaybackInfoChanged -= InfoChanged;
        Session.TimelinePropertiesChanged -= TimelineChanged;
    }
    public void Dispose()
    {
        Unbind();
        if (_manager == null) return;
        _manager.CurrentSessionChanged -= CurrentChanged;
        _manager.SessionsChanged -= SessionsChanged;
    }
}
