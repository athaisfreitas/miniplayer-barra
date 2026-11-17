namespace MiniplayerBarra.Core;

public static class SourceClassifier
{
    public static SourceKind Classify(string aumid, Settings s)
    {
        if (aumid.Contains(s.YouTubeMusicId, StringComparison.Ordinal)) return SourceKind.YouTubeMusic;
        if (aumid == "Chrome") return SourceKind.ChromeTab;
        if (aumid.StartsWith(s.WhatsAppIdPrefix, StringComparison.Ordinal)) return SourceKind.WhatsApp;
        return SourceKind.Other;
    }
}
