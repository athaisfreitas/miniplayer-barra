using System.Drawing;
using System.Drawing.Imaging;
using MiniplayerBarra.Ui;
using Xunit;

namespace MiniplayerBarra.Tests;

public class CoverArtTests
{
    private static readonly Color Accent = Color.FromArgb(10, 20, 30);

    private static byte[] Png(Func<int, int, Color> pixel, int size = 64)
    {
        using var bmp = new Bitmap(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                bmp.SetPixel(x, y, pixel(x, y));
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }

    [Fact]
    public void Red_cover_gives_reddish_color()
    {
        var (img, c) = CoverArt.Decode(Png((_, _) => Color.FromArgb(255, 0, 0)), Accent);
        using (img) { Assert.NotNull(img); }
        Assert.True(c.R > 200);
        Assert.True(c.G < 100);
        Assert.True(c.B < 100);
    }

    [Fact]
    public void Mostly_gray_with_blue_patch_picks_blue()
    {
        var bytes = Png((x, y) => x < 6 ? Color.FromArgb(20, 60, 255) : Color.FromArgb(128, 128, 128));
        var (img, c) = CoverArt.Decode(bytes, Accent);
        img?.Dispose();
        Assert.True(c.B > c.R && c.B > c.G);
    }

    [Fact]
    public void Grayscale_cover_falls_back_to_accent()
    {
        var (img, c) = CoverArt.Decode(Png((_, _) => Color.FromArgb(128, 128, 128)), Accent);
        img?.Dispose();
        Assert.Equal(Accent, c);
    }

    [Fact]
    public void Null_bytes_give_null_image_and_accent()
    {
        var (img, c) = CoverArt.Decode(null, Accent);
        Assert.Null(img);
        Assert.Equal(Accent, c);
    }

    [Fact]
    public void Invalid_cover_bytes_fall_back_to_accent()
    {
        var (img, c) = CoverArt.Decode(new byte[] { 1, 2, 3 }, Accent);
        Assert.Null(img);
        Assert.Equal(Accent, c);
    }
}
