using System.Drawing;
using System.Windows.Forms;

namespace MiniplayerBarra.Ui;

internal enum PlayerHit { None, Previous, PlayPause, Next, OpenSource }

internal record PlayerLayout(Size Size, Rectangle Waves, Rectangle Previous, Rectangle PlayPause, Rectangle Next,
                             Rectangle Cover, Rectangle Title, Rectangle Artist, bool ShowText)
{
    private const int BaseHeight = 40;
    private const int BlockWidth = 96;
    private const int ButtonSize = 32;
    private const int CoverSize = 36;
    private const int Gap = 8;
    private const int MaxTextWidth = 260;
    private const int MinTextWidth = 40;

    /// <summary>Flags used to measure text in Compute (no ellipsis).</summary>
    public const TextFormatFlags MeasureFlags =
        TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;

    /// <summary>Flags the renderer must use to draw text (measure flags plus EndEllipsis).</summary>
    public const TextFormatFlags TextFlags = MeasureFlags | TextFormatFlags.EndEllipsis;

    // DPI-independent: size in pixels = pt * 96/72 * scale. Caller disposes.
    public static Font CreateTitleFont(float scale) =>
        new("Segoe UI Semibold", 10f * 96f / 72f * scale, FontStyle.Regular, GraphicsUnit.Pixel);

    public static Font CreateArtistFont(float scale) =>
        new("Segoe UI", 9f * 96f / 72f * scale, FontStyle.Regular, GraphicsUnit.Pixel);

    private static int S(int v, float scale) => (int)Math.Round(v * scale);

    public static int MinWidth(float scale) => S(BlockWidth, scale) + S(Gap, scale) + S(CoverSize, scale);

    public static PlayerLayout Compute(Graphics g, string title, string artist, float scale, int maxWidth)
    {
        int h = S(BaseHeight, scale);
        int block = S(BlockWidth, scale);
        int btn = S(ButtonSize, scale);
        int btnY = (h - btn) / 2;
        var waves = new Rectangle(0, 0, block, h);
        var prev = new Rectangle(0, btnY, btn, btn);
        var play = new Rectangle(btn, btnY, btn, btn);
        var next = new Rectangle(btn * 2, btnY, btn, btn);
        int cover = S(CoverSize, scale);
        var coverRect = new Rectangle(block + S(Gap, scale), (h - cover) / 2, cover, cover);
        int min = MinWidth(scale);
        int textX = coverRect.Right + S(Gap, scale);

        int available = Math.Min(maxWidth - textX, S(MaxTextWidth, scale));
        if (available < S(MinTextWidth, scale) || (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(artist)))
        {
            return new PlayerLayout(new Size(min, h), waves, prev, play, next, coverRect,
                Rectangle.Empty, Rectangle.Empty, false);
        }

        using var titleFont = CreateTitleFont(scale);
        using var artistFont = CreateArtistFont(scale);
        var huge = new Size(int.MaxValue, int.MaxValue);
        Size tSize = TextRenderer.MeasureText(g, title ?? "", titleFont, huge, MeasureFlags);
        bool hasArtist = !string.IsNullOrEmpty(artist);
        Size aSize = hasArtist ? TextRenderer.MeasureText(g, artist, artistFont, huge, MeasureFlags) : Size.Empty;

        int textW = Math.Max(1, Math.Min(Math.Max(tSize.Width, aSize.Width), available));
        int total = tSize.Height + (hasArtist ? aSize.Height : 0);
        int y = Math.Max(0, (h - total) / 2);
        var titleRect = new Rectangle(textX, y, textW, tSize.Height);
        var artistRect = hasArtist ? new Rectangle(textX, y + tSize.Height, textW, aSize.Height) : Rectangle.Empty;

        return new PlayerLayout(new Size(textX + textW, h), waves, prev, play, next, coverRect,
            titleRect, artistRect, true);
    }

    public PlayerHit HitTest(Point p)
    {
        if (Previous.Contains(p)) return PlayerHit.Previous;
        if (PlayPause.Contains(p)) return PlayerHit.PlayPause;
        if (Next.Contains(p)) return PlayerHit.Next;
        if (Cover.Contains(p)) return PlayerHit.OpenSource;
        if (ShowText && (Title.Contains(p) || Artist.Contains(p))) return PlayerHit.OpenSource;
        return PlayerHit.None;
    }
}
