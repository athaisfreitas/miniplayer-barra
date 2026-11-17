using MiniplayerBarra.Core;

namespace MiniplayerBarra.Tests;

public class SettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mb-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, true);

    private string WriteFile(string content)
    {
        var path = Path.Combine(_dir, "config.json");
        File.WriteAllText(path, content);
        return path;
    }

    private static void AssertIsDefault(Settings s)
    {
        Assert.Equal(new[] { SourceKind.YouTubeMusic, SourceKind.ChromeTab, SourceKind.WhatsApp }, s.Priority);
        Assert.True(s.Waves);
        Assert.Equal("cinhimbnkkghhklpknlkffjgod", s.YouTubeMusicId);
        Assert.Equal("5319275A.WhatsAppDesktop", s.WhatsAppIdPrefix);
    }

    [Fact]
    public void Missing_file_returns_default() =>
        AssertIsDefault(Settings.Load(Path.Combine(_dir, "nao-existe.json")));

    [Fact]
    public void Reads_portuguese_keys()
    {
        var s = Settings.Load(WriteFile("{\"prioridade\":[\"whatsapp\",\"youtubeMusic\"],\"ondas\":false}"));
        Assert.Equal(new[] { SourceKind.WhatsApp, SourceKind.YouTubeMusic }, s.Priority);
        Assert.False(s.Waves);
    }

    [Fact]
    public void Unknown_priority_names_are_ignored_and_duplicates_dropped()
    {
        var s = Settings.Load(WriteFile("{\"prioridade\":[\"chrome\",\"xyz\",\"chrome\"]}"));
        Assert.Equal(new[] { SourceKind.ChromeTab }, s.Priority);
    }

    [Fact]
    public void Invalid_json_returns_default() =>
        AssertIsDefault(Settings.Load(WriteFile("{ isso nao e json")));
}
