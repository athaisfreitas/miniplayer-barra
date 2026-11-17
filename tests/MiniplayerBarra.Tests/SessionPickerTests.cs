using MiniplayerBarra.Core;
using Xunit;

namespace MiniplayerBarra.Tests;

public class SessionPickerTests
{
    private static readonly SourceKind[] Default =
        { SourceKind.YouTubeMusic, SourceKind.ChromeTab, SourceKind.WhatsApp };

    private static MediaSnapshot S(string id, SourceKind kind, bool playing) =>
        new(id, kind, "t", "a", playing, null);

    [Fact]
    public void Playing_beats_paused_regardless_of_priority()
    {
        var yt = S("yt", SourceKind.YouTubeMusic, false);
        var tab = S("tab", SourceKind.ChromeTab, true);
        Assert.Same(tab, SessionPicker.Pick(new[] { yt, tab }, Default));
    }

    [Fact]
    public void Among_playing_highest_priority_wins()
    {
        var tab = S("tab", SourceKind.ChromeTab, true);
        var yt = S("yt", SourceKind.YouTubeMusic, true);
        var wa = S("wa", SourceKind.WhatsApp, true);
        Assert.Same(yt, SessionPicker.Pick(new[] { tab, yt, wa }, Default));
    }

    [Fact]
    public void Nothing_playing_shows_highest_priority()
    {
        var tab = S("tab", SourceKind.ChromeTab, false);
        var yt = S("yt", SourceKind.YouTubeMusic, false);
        Assert.Same(yt, SessionPicker.Pick(new[] { tab, yt }, Default));
    }

    [Fact]
    public void Other_is_always_last()
    {
        var other = S("o", SourceKind.Other, true);
        var wa = S("wa", SourceKind.WhatsApp, true);
        Assert.Same(wa, SessionPicker.Pick(new[] { other, wa }, Default));
    }

    [Fact]
    public void Empty_list_returns_null()
    {
        Assert.Null(SessionPicker.Pick(Array.Empty<MediaSnapshot>(), Default));
    }

    [Fact]
    public void Kind_missing_from_priority_ranks_before_Other()
    {
        var tab = S("tab", SourceKind.ChromeTab, true);
        var other = S("o", SourceKind.Other, true);
        Assert.Same(tab, SessionPicker.Pick(new[] { other, tab }, new[] { SourceKind.WhatsApp }));
    }

    [Fact]
    public void Ties_keep_original_order()
    {
        var a = S("a", SourceKind.ChromeTab, true);
        var b = S("b", SourceKind.ChromeTab, true);
        Assert.Same(a, SessionPicker.Pick(new[] { a, b }, Default));
    }
}
