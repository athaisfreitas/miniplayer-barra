using MiniplayerBarra.Media;
using Xunit;

namespace MiniplayerBarra.Tests;

public class CoverCacheTests
{
    private static readonly byte[] Icon = { 1 };
    private static readonly byte[] Real = { 2 };

    [Fact]
    public void Same_title_is_served_from_cache()
    {
        var c = new CoverCache();
        c.Store("Chrome", "Música", Icon);
        Assert.True(c.TryGet("Chrome", "Música", out var cover));
        Assert.Same(Icon, cover);
    }

    [Fact]
    public void Other_title_is_not_served()
    {
        var c = new CoverCache();
        c.Store("Chrome", "A", Icon);
        Assert.False(c.TryGet("Chrome", "B", out _));
    }

    [Fact]
    public void Invalidated_entry_is_not_served_and_same_title_reads_again()
    {
        var c = new CoverCache();
        c.Store("Chrome", "A", Icon);
        c.Invalidate("Chrome");
        Assert.False(c.TryGet("Chrome", "A", out _));

        Assert.False(c.Store("Chrome", "A", Real)); // mesmo título: não é troca
        Assert.True(c.TryGet("Chrome", "A", out var cover));
        Assert.Same(Real, cover);
    }

    [Fact]
    public void Invalidate_by_aumid_also_hits_repeated_ids()
    {
        var c = new CoverCache();
        c.Store("Chrome", "A", Icon);
        c.Store("Chrome#2", "B", Icon);
        c.Store("ChromeX", "C", Icon);
        c.Invalidate("Chrome");
        Assert.False(c.TryGet("Chrome", "A", out _));
        Assert.False(c.TryGet("Chrome#2", "B", out _));
        Assert.True(c.TryGet("ChromeX", "C", out _));
    }

    [Fact]
    public void Failed_read_is_not_served()
    {
        var c = new CoverCache();
        c.Store("Chrome", "A", null);
        Assert.False(c.TryGet("Chrome", "A", out _));
    }

    [Fact]
    public void Store_reports_title_change_once()
    {
        var c = new CoverCache();
        Assert.True(c.Store("Chrome", "A", Icon));
        Assert.False(c.Store("Chrome", "A", Real));
        Assert.False(c.Store("Chrome", "A", null));
        Assert.True(c.Store("Chrome", "B", null));
        Assert.False(c.Store("Chrome", "B", Real));
    }

    [Fact]
    public void Prune_drops_dead_sessions()
    {
        var c = new CoverCache();
        c.Store("Chrome", "A", Icon);
        c.Store("WhatsApp", "B", Icon);
        c.Prune(new HashSet<string> { "WhatsApp" });
        Assert.False(c.TryGet("Chrome", "A", out _));
        Assert.True(c.Store("Chrome", "A", Icon));
        Assert.True(c.TryGet("WhatsApp", "B", out _));
    }

    [Fact]
    public void Invalidate_during_read_keeps_result_stale()
    {
        var c = new CoverCache();
        var before = c.Version;
        c.Invalidate("Chrome"); // aviso chegou enquanto a leitura estava em andamento
        c.Store("Chrome", "A", Icon, before);
        Assert.False(c.TryGet("Chrome", "A", out _));

        c.Store("Chrome", "A", Real, c.Version);
        Assert.True(c.TryGet("Chrome", "A", out var cover));
        Assert.Same(Real, cover);
    }
}
