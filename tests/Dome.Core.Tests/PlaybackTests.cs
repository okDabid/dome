using Dome.Core;
using Xunit;

namespace Dome.Core.Tests;

public class PlaybackTests
{
    [Fact]
    public void ParsesRepeatedTimestampsFractionsMetadataAndOffset()
    {
        var lines = Lyrics.Parse("[ar:Artist]\n[offset:-250]\n[00:02.50][00:12.500]chorus\n[00:01.1]intro\n[01:04]verse\n[00:99]invalid");
        Assert.Equal(4, lines.Count);
        Assert.Equal("intro", lines[0].Text);
        Assert.Equal(TimeSpan.FromMilliseconds(850), lines[0].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(2250), lines[1].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(12250), lines[2].Time);
        Assert.Equal(TimeSpan.FromMilliseconds(63750), lines[3].Time);
    }
    [Fact]
    public void SeeksBackwardAndHandlesBeforeFirstAndAfterLastLine()
    {
        var lines = Lyrics.Parse("[00:10]a\n[00:20]b\n[00:30]c");
        Assert.Equal(-1, Lyrics.CurrentIndex(lines, TimeSpan.Zero));
        Assert.Equal(1, Lyrics.CurrentIndex(lines, TimeSpan.FromSeconds(20)));
        Assert.Equal(2, Lyrics.CurrentIndex(lines, TimeSpan.FromMinutes(6)));
        Assert.Equal(0, Lyrics.CurrentIndex(lines, TimeSpan.FromSeconds(11)));
        Assert.Equal(-1, Lyrics.CurrentIndex([], TimeSpan.Zero));
    }
    [Fact]
    public void BlankInstrumentalLinesRemainInTimeline()
    {
        var lines = Lyrics.Parse("[00:00.000]Sing\n[00:04.000]\n[00:05.000]Again");
        Assert.Equal("", lines[Lyrics.CurrentIndex(lines, TimeSpan.FromSeconds(4.5))].Text);
    }
    [Theory]
    [InlineData(null)] [InlineData("")] [InlineData("[ar:test]\nplain lyrics")]
    public void UnsyncedInputHasNoTimeline(string? input) => Assert.Empty(Lyrics.Parse(input));
    [Fact]
    public void FocusCompletesOnceAfterMachineSleeps()
    {
        var clock = new FocusClock();
        var now = DateTimeOffset.UtcNow;
        clock.Start(now);
        Assert.True(clock.Tick(now.AddHours(1)));
        Assert.False(clock.Tick(now.AddHours(2)));
        Assert.Equal(TimeSpan.Zero, clock.Remaining);
        Assert.False(clock.Running);
    }
    [Fact]
    public void PausedTimeDoesNotElapseAndResumeKeepsRemainder()
    {
        var clock = new FocusClock();
        var now = DateTimeOffset.UtcNow;
        clock.Start(now);
        clock.Pause(now.AddMinutes(5));
        Assert.Equal(TimeSpan.FromMinutes(20), clock.Remaining);
        Assert.False(clock.Tick(now.AddHours(1)));
        clock.Start(now.AddHours(1));
        Assert.False(clock.Tick(now.AddHours(1).AddMinutes(19)));
        Assert.Equal(TimeSpan.FromMinutes(1), clock.Remaining);
        Assert.True(clock.Tick(now.AddHours(1).AddMinutes(20)));
    }
    [Fact]
    public void RepeatedStartDoesNotExtendDeadlineAndResetRestoresPreset()
    {
        var clock = new FocusClock();
        var now = DateTimeOffset.UtcNow;
        clock.SetDuration(TimeSpan.FromMinutes(15)); clock.Start(now); clock.Start(now.AddMinutes(5));
        Assert.True(clock.Tick(now.AddMinutes(15)));
        clock.Reset();
        Assert.Equal(TimeSpan.FromMinutes(15), clock.Remaining);
        Assert.False(clock.Running);
    }
    [Fact]
    public void InvalidFocusDurationsAreRejected()
    {
        var clock = new FocusClock();
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetDuration(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.SetDuration(TimeSpan.FromHours(25)));
    }
}
