using MiniplayerBarra.Ui;
using Xunit;

namespace MiniplayerBarra.Tests;

public class ChromeTabsTests
{
    [Fact]
    public void Picks_tab_containing_title()
    {
        var tabs = new[] { "Inbox - Gmail", "Obsidian in 24 Minutes - YouTube", "Home / X" };
        Assert.Equal(1, ChromeTabs.Pick(tabs, "Obsidian in 24 Minutes"));
    }

    [Fact]
    public void Prefers_playing_tab_when_title_repeats()
    {
        var tabs = new[] { "Song A - YouTube", "Song A - YouTube - Audio playing" };
        Assert.Equal(1, ChromeTabs.Pick(tabs, "Song A"));
    }

    [Fact]
    public void Falls_back_to_single_playing_tab_when_title_not_found()
    {
        var tabs = new[] { "Inbox - Gmail", "Some radio - Audio playing" };
        Assert.Equal(1, ChromeTabs.Pick(tabs, "Different title"));
    }

    [Fact]
    public void Returns_null_when_ambiguous_or_missing()
    {
        Assert.Null(ChromeTabs.Pick(new[] { "A - Audio playing", "B - Audio playing" }, "zzz"));
        Assert.Null(ChromeTabs.Pick(new[] { "A", "B" }, "zzz"));
        Assert.Null(ChromeTabs.Pick(Array.Empty<string>(), "A"));
    }

    [Fact]
    public void Matching_ignores_case()
    {
        Assert.Equal(0, ChromeTabs.Pick(new[] { "SONG A - YouTube" }, "song a"));
    }

    [Fact]
    public void Empty_title_only_uses_playing_fallback()
    {
        Assert.Equal(1, ChromeTabs.Pick(new[] { "A", "B - Audio playing" }, ""));
    }

    [Theory]
    [InlineData(MiniplayerBarra.Core.SourceKind.YouTubeMusic, "Any", "YouTube Music")]
    [InlineData(MiniplayerBarra.Core.SourceKind.ChromeTab, "My Song", "My Song")]
    public void Needle_depends_on_kind(MiniplayerBarra.Core.SourceKind kind, string title, string expected) =>
        Assert.Equal(expected, ChromeTabs.NeedleFor(kind, title));
}
