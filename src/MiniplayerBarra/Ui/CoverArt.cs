using System.Drawing;
using System.Drawing.Drawing2D;

namespace MiniplayerBarra.Ui;

public static class CoverArt
{
    private const int Sample = 32;
    private const int Bands = 36;

    public static (Bitmap? Image, Color WaveColor) Decode(byte[]? bytes, Color fallbackAccent)
    {
        if (bytes == null || bytes.Length == 0) return (null, fallbackAccent);
        try
        {
            using var ms = new MemoryStream(bytes);
            using var src = Image.FromStream(ms);
            var image = new Bitmap(src); // independent of the stream
            try
            {
                return (image, PickColor(image, fallbackAccent));
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }
        catch
        {
            return (null, fallbackAccent);
        }
    }

    private static Color PickColor(Bitmap image, Color fallback)
    {
        using var small = new Bitmap(Sample, Sample);
        using (var g = Graphics.FromImage(small))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.DrawImage(image, 0, 0, Sample, Sample);
        }

        var weight = new double[Bands];
        var sumR = new long[Bands];
        var sumG = new long[Bands];
        var sumB = new long[Bands];
        var count = new int[Bands];

        for (int y = 0; y < Sample; y++)
            for (int x = 0; x < Sample; x++)
            {
                var p = small.GetPixel(x, y);
                double s = p.GetSaturation();
                double v = Math.Max(p.R, Math.Max(p.G, p.B)) / 255.0;
                if (s < 0.35 || v < 0.30) continue;
                int band = Math.Min(Bands - 1, (int)(p.GetHue() / 360.0 * Bands));
                weight[band] += s * v;
                sumR[band] += p.R; sumG[band] += p.G; sumB[band] += p.B;
                count[band]++;
            }

        int best = -1;
        for (int i = 0; i < Bands; i++)
            if (count[i] > 0 && (best < 0 || weight[i] > weight[best])) best = i;
        if (best < 0) return fallback;

        int r = (int)(sumR[best] / count[best]);
        int gC = (int)(sumG[best] / count[best]);
        int b = (int)(sumB[best] / count[best]);
        return Brighten(r, gC, b, 0.75);
    }

    // Raises HSV value to at least minV, keeping hue and saturation.
    private static Color Brighten(int r, int g, int b, double minV)
    {
        double max = Math.Max(r, Math.Max(g, b)) / 255.0;
        if (max >= minV || max <= 0) return Color.FromArgb(r, g, b);
        double k = minV / max;
        return Color.FromArgb(Clamp(r * k), Clamp(g * k), Clamp(b * k));
    }

    private static int Clamp(double v) => (int)Math.Min(255, Math.Round(v));
}
