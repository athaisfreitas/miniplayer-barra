using MiniplayerBarra.Audio;
using MiniplayerBarra.Core;

namespace MiniplayerBarra.Tests;

public class SourceProcessTests
{
    [Theory]
    [InlineData(SourceKind.YouTubeMusic, "chrome")]
    [InlineData(SourceKind.ChromeTab, "chrome")]
    [InlineData(SourceKind.WhatsApp, "WhatsApp")]
    [InlineData(SourceKind.Other, null)]
    public void ProcessNameFor_maps_each_kind(SourceKind kind, string? expected) =>
        Assert.Equal(expected, SourceProcess.ProcessNameFor(kind));

    [Fact]
    public void FindRootPid_picks_chrome_whose_parent_is_not_chrome()
    {
        var list = new[] { (10, 1, "explorer"), (20, 10, "chrome"), (30, 20, "chrome") };
        Assert.Equal(20, SourceProcess.FindRootPid("chrome", list));
    }

    [Fact]
    public void FindRootPid_ignores_exe_suffix_and_case()
    {
        var list = new[] { (10, 1, "explorer.exe"), (30, 20, "chrome.exe"), (20, 10, "Chrome.exe") };
        Assert.Equal(20, SourceProcess.FindRootPid("chrome", list));
    }

    [Fact]
    public void FindRootPid_matches_dotted_exe_name_of_new_whatsapp()
    {
        // O WhatsApp Desktop atual roda como WhatsApp.Root.exe.
        var list = new[] { (10, 1, "explorer.exe"), (50, 10, "WhatsApp.Root.exe"), (60, 50, "msedgewebview2.exe") };
        Assert.Equal(50, SourceProcess.FindRootPid("WhatsApp", list));
    }

    [Fact]
    public void FindRootPid_does_not_match_other_names_with_same_prefix()
    {
        var list = new[] { (10, 1, "explorer.exe"), (20, 10, "chromedriver.exe") };
        Assert.Null(SourceProcess.FindRootPid("chrome", list));
    }

    [Fact]
    public void FindRootPid_returns_null_when_not_running()
    {
        var list = new[] { (10, 1, "explorer"), (20, 10, "notepad") };
        Assert.Null(SourceProcess.FindRootPid("chrome", list));
    }

    [Fact]
    public void FindRootPid_with_two_roots_picks_the_one_with_more_children()
    {
        var list = new[]
        {
            (10, 1, "explorer"),
            (40, 10, "chrome"),            // raiz pequena (ex.: processo solto)
            (20, 10, "chrome"), (30, 20, "chrome"), (31, 20, "chrome"), (32, 31, "chrome"),
        };
        Assert.Equal(20, SourceProcess.FindRootPid("chrome", list));
    }
}
