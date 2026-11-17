using System.Drawing;
using System.Drawing.Imaging;
using MiniplayerBarra.Core;
using MiniplayerBarra.Ui;
using Xunit;

namespace MiniplayerBarra.Tests;

public class PlayerRendererTests
{
    private static readonly Theme Dark = Theme.FromRegistryValue(0);

    private static MediaSnapshot Media(string title, string artist, bool playing) =>
        new("Chrome", SourceKind.ChromeTab, title, artist, playing, null);

    private static Bitmap Render(PlayerState state, float[] bands, out PlayerLayout layout, float scale = 1f)
    {
        using (var probe = new Bitmap(1, 1))
        using (var pg = Graphics.FromImage(probe))
            layout = PlayerLayout.Compute(pg, state.Media.Title, state.Media.Artist, scale, 500);
        var bmp = new Bitmap(layout.Size.Width, layout.Size.Height, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        PlayerRenderer.Draw(g, layout, state, bands, Dark, scale);
        return bmp;
    }

    private static float[] Full() => Enumerable.Repeat(1f, 12).ToArray();

    // A pixel in the waves block below the buttons, inside the first bar.
    private static Color WavePixel(Bitmap bmp, PlayerLayout l)
    {
        int x = l.Waves.Left + l.Waves.Width / 24;
        int y = l.Waves.Bottom - 2;
        Assert.True(y >= l.PlayPause.Bottom, "pixel must be outside the buttons");
        return bmp.GetPixel(x, y);
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void Draws_without_throwing_for_all_states(bool playing, bool withCover, bool withText)
    {
        using var cover = withCover ? new Bitmap(64, 64) : null;
        if (cover != null)
            using (var cg = Graphics.FromImage(cover)) cg.Clear(Color.Blue);
        var state = new PlayerState(Media(withText ? "Título" : "", withText ? "Artista" : "", playing), cover, Color.Red);
        foreach (float scale in new[] { 1f, 1.5f })
            using (Render(state, Full(), out _, scale)) { }
        using (Render(state, Array.Empty<float>(), out _)) { }
    }

    [Fact]
    public void Playing_bars_are_tinted_with_wave_color()
    {
        var state = new PlayerState(Media("Título", "Artista", true), null, Color.Red);
        using var bmp = Render(state, Full(), out var l);
        var p = WavePixel(bmp, l);
        Assert.True(p.A > 0, $"alpha {p.A}");
        Assert.True(p.R > p.G && p.R > p.B, $"pixel {p}");
    }

    [Fact]
    public void Paused_bars_are_faint()
    {
        var state = new PlayerState(Media("Título", "Artista", false), null, Color.Red);
        using var bmp = Render(state, Full(), out var l);
        var p = WavePixel(bmp, l);
        Assert.True(p.A < 0.3 * 255, $"alpha {p.A}");
        Assert.True(p.A > 0, "paused bars stay visible at minimum height");
    }
}
