using System.Drawing;
using MiniplayerBarra.Ui;
using Xunit;

namespace MiniplayerBarra.Tests;

public class PlayerLayoutTests
{
    private static PlayerLayout Compute(string title, string artist, int maxWidth, float scale = 1f)
    {
        using var bmp = new Bitmap(10, 10);
        using var g = Graphics.FromImage(bmp);
        return PlayerLayout.Compute(g, title, artist, scale, maxWidth);
    }

    private static Point Center(Rectangle r) => new(r.Left + r.Width / 2, r.Top + r.Height / 2);

    [Fact]
    public void Long_title_is_truncated_to_available_width()
    {
        var l = Compute(new string('a', 200), "Artista", 300);
        Assert.True(l.Size.Width <= 300);
        Assert.True(l.ShowText);
    }

    [Fact]
    public void Below_min_width_hides_text_but_keeps_buttons_and_cover()
    {
        var l = Compute("Titulo", "Artista", PlayerLayout.MinWidth(1) - 10);
        Assert.False(l.ShowText);
        Assert.Equal(PlayerLayout.MinWidth(1), l.Size.Width);
        Assert.False(l.Cover.IsEmpty);
        Assert.False(l.PlayPause.IsEmpty);
    }

    [Fact]
    public void Empty_artist_uses_single_line()
    {
        var l = Compute("Titulo", "", 400);
        Assert.True(l.Artist.IsEmpty);
        Assert.InRange(Center(l.Title).Y - l.Size.Height / 2, -1, 1);
    }

    [Fact]
    public void HitTest_maps_regions()
    {
        var l = Compute("Titulo", "Artista", 400);
        Assert.Equal(PlayerHit.Previous, l.HitTest(Center(l.Previous)));
        Assert.Equal(PlayerHit.PlayPause, l.HitTest(Center(l.PlayPause)));
        Assert.Equal(PlayerHit.Next, l.HitTest(Center(l.Next)));
        Assert.Equal(PlayerHit.OpenSource, l.HitTest(Center(l.Cover)));
        Assert.Equal(PlayerHit.OpenSource, l.HitTest(Center(l.Title)));
        Assert.Equal(PlayerHit.None, l.HitTest(new Point(l.Waves.Left + 10, l.Waves.Top + 1)));
    }

    [Fact]
    public void Scale_multiplies_measures()
    {
        Assert.Equal(PlayerLayout.MinWidth(1) * 2, PlayerLayout.MinWidth(2));
    }

    [Fact]
    public void Scale_1_5_has_height_60_and_everything_inside_size()
    {
        var l = Compute("Titulo", "Artista", 500, 1.5f);
        Assert.Equal(60, l.Size.Height);
        var bounds = new Rectangle(Point.Empty, l.Size);
        foreach (var r in new[] { l.Waves, l.Previous, l.PlayPause, l.Next, l.Cover, l.Title, l.Artist })
            Assert.True(bounds.Contains(r));
    }
}
