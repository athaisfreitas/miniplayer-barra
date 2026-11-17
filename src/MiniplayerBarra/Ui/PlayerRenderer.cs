using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace MiniplayerBarra.Ui;

/// <summary>
/// Desenha o player num bitmap transparente: as ondas no bloco da esquerda, os três botões por cima,
/// a capa com cantos arredondados e o texto. A parte que não muda a cada quadro (botões, capa e
/// texto) fica guardada numa camada e só é refeita quando algo dela muda; a cada quadro só as
/// barras são desenhadas.
/// </summary>
internal static class PlayerRenderer
{
    public const int BarCount = 12;

    private const string FluentIcons = "Segoe Fluent Icons";
    private const string Mdl2Assets = "Segoe MDL2 Assets";
    private const string PreviousGlyph = "";
    private const string PauseGlyph = "";
    private const string PlayGlyph = "";
    private const string NextGlyph = "";
    private const string NoteGlyph = "";

    private const float BarGap = 2f;
    private const float MinBarHeight = 3f;
    private const float WaveTopPad = 2f;
    private const int AlphaBase = (int)(0.9 * 255);
    private const int AlphaTop = (int)(0.35 * 255);
    private const int AlphaPaused = (int)(0.25 * 255);
    private const float GlyphPx = 16f;
    private const float NotePx = 18f;
    private const float CoverRadius = 4f;
    private const int ArtistAlpha = 180;
    private const int HaloAlpha = 150;
    private const float HaloPx = 3f;

    private static readonly Lazy<string> IconFontName = new(ResolveIconFont);
    private static readonly object CacheGate = new();
    private static LayerKey? _cachedKey;
    private static Bitmap? _cachedLayer;

    public static void Draw(Graphics g, PlayerLayout l, PlayerState s, float[] bands, Theme t, float scale)
    {
        DrawBars(g, l.Waves, s, bands, scale);
        var key = new LayerKey(l, s.Media.Title ?? "", s.Media.Artist ?? "", s.Media.IsPlaying, s.Cover, t.Text, scale);
        lock (CacheGate)
        {
            if (_cachedLayer is null || _cachedKey != key)
            {
                _cachedLayer?.Dispose();
                _cachedLayer = null;
                _cachedLayer = BuildLayer(key);
                _cachedKey = key;
            }
            g.CompositingMode = CompositingMode.SourceOver;
            g.DrawImageUnscaled(_cachedLayer, 0, 0);
        }
    }

    private static void DrawBars(Graphics g, Rectangle waves, PlayerState s, float[] bands, float scale)
    {
        if (waves.Width <= 0 || waves.Height <= 0) return;
        bool playing = s.Media.IsPlaying;
        float slot = waves.Width / (float)BarCount;
        float gap = Math.Min(BarGap * scale, slot / 2);
        float barW = slot - gap;
        float minH = MinBarHeight * scale;
        float maxH = waves.Height - WaveTopPad * scale;

        var color = s.WaveColor;
        using Brush brush = playing
            ? new LinearGradientBrush(
                new RectangleF(waves.Left, waves.Top - 1, waves.Width, waves.Height + 2),
                Color.FromArgb(AlphaTop, color), Color.FromArgb(AlphaBase, color), LinearGradientMode.Vertical)
            : new SolidBrush(Color.FromArgb(AlphaPaused, color));

        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.None;
        for (int i = 0; i < BarCount; i++)
        {
            float v = playing ? Band(bands, i) : 0f;
            float h = minH + v * (maxH - minH);
            float x = waves.Left + i * slot + gap / 2;
            g.FillRectangle(brush, x, waves.Bottom - h, barW, h);
        }
        g.SmoothingMode = old;
    }

    private static float Band(float[] bands, int i)
    {
        if (bands is null || bands.Length == 0) return 0f;
        float v = bands[Math.Min(bands.Length - 1, i * bands.Length / BarCount)];
        return float.IsNaN(v) ? 0f : Math.Clamp(v, 0f, 1f);
    }

    private static Bitmap BuildLayer(LayerKey k)
    {
        var l = k.Layout;
        var layer = new Bitmap(Math.Max(1, l.Size.Width), Math.Max(1, l.Size.Height), PixelFormat.Format32bppArgb);
        if (l.ShowText) DrawText(layer, l, k.Title, k.Artist, k.Text, k.Scale);

        using var g = Graphics.FromImage(layer);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        DrawButton(g, PreviousGlyph, l.Previous, k.Text, k.Scale);
        DrawButton(g, k.Playing ? PauseGlyph : PlayGlyph, l.PlayPause, k.Text, k.Scale);
        DrawButton(g, NextGlyph, l.Next, k.Text, k.Scale);

        DrawCover(g, l.Cover, k.Cover, k.Text, k.Scale);
        return layer;
    }

    /// <summary>Ícone com um contorno suave na cor oposta, para ficar legível por cima das barras.</summary>
    private static void DrawButton(Graphics g, string glyph, Rectangle r, Color text, float scale)
    {
        using var family = new FontFamily(IconFontName.Value);
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var path = new GraphicsPath();
        path.AddString(glyph, family, (int)FontStyle.Regular, GlyphPx * scale, r, center);
        bool lightText = text.GetBrightness() > 0.5f;
        using var halo = new Pen(Color.FromArgb(HaloAlpha, lightText ? Color.Black : Color.White), HaloPx * scale)
        {
            LineJoin = LineJoin.Round,
        };
        g.DrawPath(halo, path);
        using var brush = new SolidBrush(text);
        g.FillPath(brush, path);
    }

    private static void DrawCover(Graphics g, Rectangle r, Bitmap? cover, Color text, float scale)
    {
        if (r.Width <= 0 || r.Height <= 0) return;
        using var path = RoundedRect(r, CoverRadius * scale);
        if (cover is not null)
        {
            try
            {
                // Escala uma vez para o tamanho final e usa como textura, para ter cantos suaves.
                using var scaled = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb);
                using (var sg = Graphics.FromImage(scaled))
                {
                    sg.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    sg.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    sg.DrawImage(cover, new Rectangle(0, 0, r.Width, r.Height));
                }
                using var tex = new TextureBrush(scaled, WrapMode.Clamp);
                tex.TranslateTransform(r.Left, r.Top);
                g.FillPath(tex, path);
                return;
            }
            catch (ArgumentException)
            {
                // Capa descartada por quem chamou: cai para a nota.
            }
        }

        using (var bg = new SolidBrush(Color.FromArgb(64, 128, 128, 128)))
            g.FillPath(bg, path);
        using var noteFont = new Font(IconFontName.Value, NotePx * scale, GraphicsUnit.Pixel);
        using var noteBrush = new SolidBrush(Color.FromArgb(200, text));
        using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(NoteGlyph, noteFont, noteBrush, r, center);
    }

    private static GraphicsPath RoundedRect(Rectangle r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.Left, r.Top, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>
    /// O texto usa o TextRenderer (GDI) com as mesmas fontes e flags do PlayerLayout, para o corte
    /// com "…" bater com a medida. O GDI não escreve alfa, então o texto é desenhado em branco/cinza
    /// sobre preto e a intensidade vira o alfa da cor do tema.
    /// </summary>
    private static void DrawText(Bitmap layer, PlayerLayout l, string title, string artist, Color text, float scale)
    {
        var area = l.Artist.IsEmpty ? l.Title : Rectangle.Union(l.Title, l.Artist);
        area.Intersect(new Rectangle(Point.Empty, layer.Size));
        if (area.Width <= 0 || area.Height <= 0) return;

        using var mask = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppRgb);
        using (var mg = Graphics.FromImage(mask))
        {
            mg.Clear(Color.Black);
            mg.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using var titleFont = PlayerLayout.CreateTitleFont(scale);
            using var artistFont = PlayerLayout.CreateArtistFont(scale);
            var off = new Size(-area.Left, -area.Top);
            TextRenderer.DrawText(mg, title, titleFont, l.Title with { Location = l.Title.Location + off },
                Color.White, Color.Black, PlayerLayout.TextFlags);
            if (!l.Artist.IsEmpty)
                TextRenderer.DrawText(mg, artist, artistFont, l.Artist with { Location = l.Artist.Location + off },
                    Color.FromArgb(ArtistAlpha, ArtistAlpha, ArtistAlpha), Color.Black, PlayerLayout.TextFlags);
        }

        var full = new Rectangle(Point.Empty, area.Size);
        var src = mask.LockBits(full, ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
        var dst = layer.LockBits(area, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            int rowBytes = area.Width * 4;
            var srcRow = new byte[rowBytes];
            var dstRow = new byte[rowBytes];
            for (int y = 0; y < area.Height; y++)
            {
                System.Runtime.InteropServices.Marshal.Copy(src.Scan0 + y * src.Stride, srcRow, 0, rowBytes);
                for (int x = 0; x < rowBytes; x += 4)
                {
                    // BGRA. Média dos canais: aguenta ClearType, se o GDI usar.
                    int a = (srcRow[x] + srcRow[x + 1] + srcRow[x + 2]) / 3 * text.A / 255;
                    dstRow[x] = text.B;
                    dstRow[x + 1] = text.G;
                    dstRow[x + 2] = text.R;
                    dstRow[x + 3] = (byte)a;
                }
                System.Runtime.InteropServices.Marshal.Copy(dstRow, 0, dst.Scan0 + y * dst.Stride, rowBytes);
            }
        }
        finally
        {
            mask.UnlockBits(src);
            layer.UnlockBits(dst);
        }
    }

    private static string ResolveIconFont()
    {
        using var fonts = new InstalledFontCollection();
        foreach (var f in fonts.Families)
            if (string.Equals(f.Name, FluentIcons, StringComparison.OrdinalIgnoreCase)) return FluentIcons;
        return Mdl2Assets;
    }

    private sealed record LayerKey(PlayerLayout Layout, string Title, string Artist, bool Playing, Bitmap? Cover,
                                   Color Text, float Scale)
    {
        // A capa é comparada pela referência (Bitmap não tem igualdade de valor).
        public bool Equals(LayerKey? o) => o is not null && Layout == o.Layout && Title == o.Title && Artist == o.Artist
            && Playing == o.Playing && ReferenceEquals(Cover, o.Cover) && Text == o.Text && Scale == o.Scale;

        public override int GetHashCode() => HashCode.Combine(Layout, Title, Artist, Playing, Text, Scale);
    }
}
