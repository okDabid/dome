namespace Dome.Core;

public sealed class FocusClock
{
    public TimeSpan Duration { get; private set; } = TimeSpan.FromMinutes(25);
    public TimeSpan Remaining { get; private set; } = TimeSpan.FromMinutes(25);
    public DateTimeOffset? Deadline { get; private set; }
    public bool Running => Deadline is not null;

    public void SetDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero || duration > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(duration));
        Duration = duration;
        Reset();
    }
    public void Start(DateTimeOffset now)
    {
        if (Running) return;
        if (Remaining <= TimeSpan.Zero) Remaining = Duration;
        Deadline = now + Remaining;
    }
    public void Pause(DateTimeOffset now)
    {
        Tick(now);
        Deadline = null;
    }
    public void Reset() { Deadline = null; Remaining = Duration; }
    public bool Tick(DateTimeOffset now)
    {
        if (Deadline is not { } deadline) return false;
        Remaining = deadline > now ? deadline - now : TimeSpan.Zero;
        if (Remaining > TimeSpan.Zero) return false;
        Deadline = null;
        return true;
    }
}
