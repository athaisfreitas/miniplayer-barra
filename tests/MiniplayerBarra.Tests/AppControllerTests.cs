using System.Drawing;
using MiniplayerBarra.Core;
using MiniplayerBarra.Tests.Fakes;
using MiniplayerBarra.Ui;

namespace MiniplayerBarra.Tests;

public class AppControllerTests
{
    private const string Yt = "Chrome._crx_cinhimbnkkghhklpknlkffjgod";

    private static MediaSnapshot S(string id, SourceKind kind, bool playing, string title = "t", byte[]? cover = null) =>
        new(id, kind, title, "a", playing, cover);

    private sealed class Rig : IDisposable
    {
        public FakeMediaSessions Sessions { get; } = new();
        public FakePlayerView View { get; } = new();
        public FakeWaveSource Waves { get; } = new();
        public FakeFrameTimer Timer { get; } = new();
        public FakeAppVolume Volume { get; } = new();
        public List<MediaSnapshot> Opened { get; } = new();
        public int Decodes { get; private set; }
        public AppController Controller { get; }

        public Rig(bool waves = true)
        {
            var cfg = Settings.Default with { Waves = waves };
            Controller = new AppController(Sessions, View, cfg, () => Color.Blue, Waves,
                post: a => a(),
                timerFactory: () => Timer,
                delay: _ => Task.CompletedTask,
                openSource: m => { lock (Opened) Opened.Add(m); },
                volume: Volume,
                decode: (bytes, accent) =>
                {
                    Decodes++;
                    return bytes is null ? (null, accent) : (new Bitmap(4, 4), Color.Red);
                });
        }

        public void Dispose() => Controller.Dispose();
    }

    [Fact]
    public async Task Shows_picked_session_on_start()
    {
        using var r = new Rig();
        r.Sessions.Sessions.Add(S("Chrome", SourceKind.ChromeTab, false));
        r.Sessions.Sessions.Add(S(Yt, SourceKind.YouTubeMusic, false));
        await r.Controller.StartAsync();
        Assert.Equal(Yt, r.View.Last?.Media.SessionId);
    }

    [Fact]
    public async Task Hides_when_no_sessions()
    {
        using var r = new Rig();
        await r.Controller.StartAsync();
        Assert.Single(r.View.Shows);
        Assert.Null(r.View.Shows[0]);
    }

    [Fact]
    public async Task Changed_refreshes_the_view()
    {
        using var r = new Rig();
        await r.Controller.StartAsync();
        r.Sessions.Sessions.Add(S(Yt, SourceKind.YouTubeMusic, true));
        r.Sessions.RaiseChanged();
        Assert.Equal(Yt, r.View.Last?.Media.SessionId);
    }

    [Fact]
    public async Task PlayPause_click_targets_shown_session()
    {
        using var r = new Rig();
        r.Sessions.Sessions.Add(S("Chrome", SourceKind.ChromeTab, false));
        r.Sessions.Sessions.Add(S(Yt, SourceKind.YouTubeMusic, false));
        await r.Controller.StartAsync();
        int before = r.View.Shows.Count;
        r.View.Click(PlayerHit.PlayPause);
        r.View.Click(PlayerHit.Next);
        r.View.Click(PlayerHit.Previous);
        Assert.Equal(new[] { "toggle:" + Yt, "next:" + Yt, "previous:" + Yt }, r.Sessions.Calls);
        Assert.True(r.View.Shows.Count > before); // refresh depois do comando
    }

    [Fact]
    public async Task Command_on_vanished_session_is_ignored()
    {
        using var r = new Rig();
        r.Sessions.Sessions.Add(S(Yt, SourceKind.YouTubeMusic, false));
        await r.Controller.StartAsync();
        r.Sessions.Sessions.Clear();
        r.Sessions.Sessions.Add(S("Chrome", SourceKind.ChromeTab, false));
        r.View.Click(PlayerHit.PlayPause);
        Assert.Equal(new[] { "toggle:" + Yt }, r.Sessions.Calls);
        Assert.Equal("Chrome", r.View.Last?.Media.SessionId);
    }

    [Fact]
    public Task Burst_of_changes_shows_latest_snapshot() =>
        // Fora do SynchronizationContext do xUnit: assim as respostas do fake rodam na hora (como na thread da Ui).
        Task.Run(async () =>
        {
            using var r = new Rig();
            await r.Controller.StartAsync();
            r.Sessions.DeferGets = true;
            for (int i = 1; i <= 5; i++)
            {
                r.Sessions.Sessions.Clear();
                r.Sessions.Sessions.Add(S("s" + i, SourceKind.Other, true, title: "n" + i));
                r.Sessions.RaiseChanged();
            }
            Assert.Equal(5, r.Sessions.PendingGets.Count);
            for (int i = 1; i < 5; i++) r.Sessions.CompleteGet(i);
            r.Sessions.CompleteGet(0); // a 1ª responde por último
            Assert.Equal("s5", r.View.Last?.Media.SessionId);
            Assert.Equal(new[] { null, "s5" }, r.View.Shows.Select(s => s?.Media.SessionId));
        });

    [Fact]
    public async Task Waves_start_only_while_playing()
    {
        using var r = new Rig();
        r.Sessions.Sessions.Add(S(Yt, SourceKind.YouTubeMusic, true));
        await r.Controller.StartAsync();
        Assert.Equal(new[] { "start:YouTubeMusic" }, r.Waves.Calls);
        Assert.True(r.Timer.Running);
        r.Timer.Fire();
        Assert.Equal(new[] { 0.5f }, r.View.Bands.Single());

        r.Sessions.Sessions[0] = r.Sessions.Sessions[0] with { IsPlaying = false };
        r.Sessions.RaiseChanged();
        Assert.Equal("stop", r.Waves.Calls[^1]);
        Assert.False(r.Timer.Running);

        r.Sessions.Sessions.Clear();
        r.Sessions.RaiseChanged();
        Assert.False(r.Timer.Running);
        Assert.DoesNotContain(r.Waves.Calls.Skip(1), c => c.StartsWith("start"));
    }

    [Fact]
    public async Task Waves_never_start_when_disabled()
    {
        using var r = new Rig(waves: false);
        r.Sessions.Sessions.Add(S(Yt, SourceKind.YouTubeMusic, true));
        await r.Controller.StartAsync();
        Assert.DoesNotContain(r.Waves.Calls, c => c.StartsWith("start"));
        Assert.False(r.Timer.Running);
    }

    [Fact]
    public async Task PauseAll_menu_pauses_everything()
    {
        using var r = new Rig();
        r.Sessions.Sessions.Add(S(Yt, SourceKind.YouTubeMusic, true));
        r.Sessions.Sessions.Add(S("wa", SourceKind.WhatsApp, true));
        await r.Controller.StartAsync();
        r.View.RequestPauseAll();
        Assert.DoesNotContain(r.Sessions.Sessions, s => s.IsPlaying);
        Assert.False(r.View.Last?.Media.IsPlaying);
    }

    [Fact]
    public async Task OpenSource_click_opens_shown_media()
    {
        using var r = new Rig();
        r.Sessions.Sessions.Add(S("wa", SourceKind.WhatsApp, true));
        await r.Controller.StartAsync();
        r.View.Click(PlayerHit.OpenSource);
        Assert.True(SpinWait.SpinUntil(() => { lock (r.Opened) return r.Opened.Count == 1; }, 2000));
        lock (r.Opened) Assert.Equal("wa", r.Opened.Single().SessionId);
    }

    [Fact]
    public async Task Wheel_up_raises_volume_of_shown_source_and_shows_it()
    {
        using var r = new Rig();
        r.Sessions.Sessions.Add(S("c", SourceKind.ChromeTab, true));
        await r.Controller.StartAsync();
        r.View.Wheel(1);
        Assert.Equal((SourceKind.ChromeTab, 0.55f), r.Volume.Sets.Single());
        Assert.Equal(55, r.View.Volumes.Single());
    }

    [Fact]
    public async Task Wheel_does_nothing_without_audio_session()
    {
        using var r = new Rig();
        r.Sessions.Sessions.Add(S("c", SourceKind.ChromeTab, true));
        await r.Controller.StartAsync();
        r.Volume.Current = null;
        r.View.Wheel(-1);
        Assert.Empty(r.Volume.Sets);
        Assert.Empty(r.View.Volumes);
    }

    [Fact]
    public async Task Wheel_without_media_does_nothing()
    {
        using var r = new Rig();
        await r.Controller.StartAsync();
        r.View.Wheel(1);
        Assert.Empty(r.Volume.Sets);
    }

    [Fact]
    public async Task Cover_is_decoded_only_when_track_changes_and_old_one_disposed_after_show()
    {
        using var r = new Rig();
        var cover = new byte[] { 1, 2, 3 };
        r.Sessions.Sessions.Add(S(Yt, SourceKind.YouTubeMusic, true, "a", cover));
        await r.Controller.StartAsync();
        r.Sessions.Sessions[0] = r.Sessions.Sessions[0] with { IsPlaying = false, Cover = new byte[] { 1, 2, 3 } };
        r.Sessions.RaiseChanged();
        Assert.Equal(1, r.Decodes);
        var first = r.View.Last!.Cover!;
        Assert.Same(first, r.View.Shows[0]!.Cover);
        Assert.Equal(Color.Red, r.View.Last!.WaveColor);

        bool firstAliveDuringShow = false;
        r.View.OnShow = _ => firstAliveDuringShow = IsAlive(first);
        r.Sessions.Sessions[0] = r.Sessions.Sessions[0] with { Title = "b", Cover = new byte[] { 9 } };
        r.Sessions.RaiseChanged();
        Assert.Equal(2, r.Decodes);
        Assert.NotSame(first, r.View.Last!.Cover);
        Assert.True(firstAliveDuringShow);
        Assert.False(IsAlive(first));
    }

    private static bool IsAlive(Bitmap b)
    {
        try { _ = b.Width; return true; }
        catch (ArgumentException) { return false; }
    }

    [Theory]
    [InlineData(SourceKind.YouTubeMusic, "x", "Música - YouTube Music", "chrome", true)]
    [InlineData(SourceKind.YouTubeMusic, "x", "Outra coisa - Google Chrome", "chrome", false)]
    [InlineData(SourceKind.ChromeTab, "Vídeo legal", "Vídeo legal - YouTube - Google Chrome", "chrome", true)]
    [InlineData(SourceKind.ChromeTab, "Vídeo legal", "Vídeo legal - Bloco de Notas", "notepad", false)]
    [InlineData(SourceKind.ChromeTab, "", "Qualquer - Google Chrome", "chrome", false)]
    [InlineData(SourceKind.WhatsApp, "x", "WhatsApp", "WhatsApp.Root", true)]
    [InlineData(SourceKind.WhatsApp, "x", "WhatsApp", "WhatsApp", true)]
    [InlineData(SourceKind.WhatsApp, "x", "WhatsApp", "WhatsAppX", false)]
    [InlineData(SourceKind.Other, "x", "x", "x", false)]
    public void SourceWindow_matches(SourceKind kind, string mediaTitle, string windowTitle, string process, bool expected) =>
        Assert.Equal(expected, SourceWindow.Matches(kind, mediaTitle, windowTitle, process));
}
