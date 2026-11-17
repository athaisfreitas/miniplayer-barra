using MiniplayerBarra.Core;
using Xunit;

namespace MiniplayerBarra.Tests;

public class SourceClassifierTests
{
    [Theory]
    [InlineData("Chrome._crx_cinhimbnkkghhklpknlkffjgod", SourceKind.YouTubeMusic)]
    [InlineData("Chrome", SourceKind.ChromeTab)]
    [InlineData("Chrome._crx_outroapp", SourceKind.Other)]
    [InlineData("5319275A.WhatsAppDesktop_cv1g1gvanyjgm!App", SourceKind.WhatsApp)]
    [InlineData("Spotify.exe", SourceKind.Other)]
    public void Classify_maps_aumid_to_kind(string aumid, SourceKind expected)
    {
        Assert.Equal(expected, SourceClassifier.Classify(aumid, Settings.Default));
    }
}
