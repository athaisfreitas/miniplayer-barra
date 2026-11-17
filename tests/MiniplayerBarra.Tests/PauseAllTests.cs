using MiniplayerBarra.Core;
using MiniplayerBarra.Tests.Fakes;
using Xunit;

namespace MiniplayerBarra.Tests;

public class PauseAllTests
{
    private static MediaSnapshot S(string id, SourceKind kind, bool playing) =>
        new(id, kind, "t", "a", playing, null);

    private static Func<TimeSpan, Task> Delays(List<TimeSpan> log) => t => { log.Add(t); return Task.CompletedTask; };

    [Fact]
    public async Task Pauses_every_playing_session()
    {
        var f = new FakeMediaSessions();
        f.Sessions.Add(S("a", SourceKind.YouTubeMusic, true));
        f.Sessions.Add(S("b", SourceKind.WhatsApp, true));
        f.Sessions.Add(S("c", SourceKind.Other, true));
        var n = await PauseAll.RunAsync(f, Delays(new()));
        Assert.Equal(3, n);
        Assert.All(f.Sessions, s => Assert.False(s.IsPlaying));
    }

    [Fact]
    public async Task Keeps_going_when_new_playing_session_appears()
    {
        var f = new FakeMediaSessions { SpawnOnStopOf = "Chrome", SpawnRemaining = 2 };
        f.Sessions.Add(S("Chrome", SourceKind.ChromeTab, true));
        var delays = new List<TimeSpan>();
        var n = await PauseAll.RunAsync(f, Delays(delays));
        Assert.Equal(3, n);
        Assert.DoesNotContain(f.Sessions, s => s.IsPlaying);
        Assert.NotEmpty(delays);
        Assert.All(delays, d => Assert.Equal(TimeSpan.FromMilliseconds(400), d));
    }

    [Fact]
    public async Task Chrome_sessions_are_paused_then_stopped()
    {
        var f = new FakeMediaSessions();
        f.Sessions.Add(S("Chrome", SourceKind.ChromeTab, true));
        await PauseAll.RunAsync(f, Delays(new()));
        Assert.Equal(new[] { "pause:Chrome", "stop:Chrome" }, f.Calls);
    }

    [Fact]
    public async Task Non_chrome_sessions_are_only_paused()
    {
        var f = new FakeMediaSessions();
        f.Sessions.Add(S("yt", SourceKind.YouTubeMusic, true));
        f.Sessions.Add(S("o", SourceKind.Other, true));
        await PauseAll.RunAsync(f, Delays(new()));
        Assert.DoesNotContain(f.Calls, c => c.StartsWith("stop:"));
    }

    [Fact]
    public async Task Stops_after_max_rounds()
    {
        var f = new FakeMediaSessions { ResumeOnPauseOf = "x" };
        f.Sessions.Add(S("x", SourceKind.Other, true));
        var delays = new List<TimeSpan>();
        var n = await PauseAll.RunAsync(f, Delays(delays));
        Assert.Equal(10, n);
        Assert.Equal(10, delays.Count);
    }

    [Fact]
    public async Task Nothing_playing_returns_zero_without_delay()
    {
        var f = new FakeMediaSessions();
        f.Sessions.Add(S("a", SourceKind.Other, false));
        var delays = new List<TimeSpan>();
        Assert.Equal(0, await PauseAll.RunAsync(f, Delays(delays)));
        Assert.Empty(delays);
        Assert.Empty(f.Calls);
    }
}
