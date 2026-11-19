using MiniplayerBarra.Core;

namespace MiniplayerBarra.Ui;

/// <summary>Acha e seleciona, pela UI Automation, a aba do Chrome que está tocando.</summary>
internal static class ChromeTabs
{
    /// <summary>Sufixos que o Chrome põe no nome da aba que toca (inglês e português).</summary>
    private static readonly string[] AudioMarkers = { "Audio playing", "Reproduzindo áudio", "Áudio em reprodução" };

    /// <summary>YT Music: aba com "YouTube Music" no nome. Aba comum: o título da mídia.</summary>
    public static string NeedleFor(SourceKind kind, string mediaTitle) =>
        kind == SourceKind.YouTubeMusic ? "YouTube Music" : mediaTitle;

    /// <summary>
    /// Índice da aba a abrir: 1) abas cujo nome contém <paramref name="needle"/>, preferindo a que toca;
    /// 2) se nenhuma, a única aba que toca; 3) senão null.
    /// </summary>
    public static int? Pick(IReadOnlyList<string> tabNames, string needle)
    {
        int? firstByTitle = null;
        int? playingByTitle = null;
        int? singlePlaying = null;
        int playingCount = 0;
        for (int i = 0; i < tabNames.Count; i++)
        {
            var name = tabNames[i];
            bool playing = IsPlaying(name);
            if (playing) { playingCount++; singlePlaying = i; }
            if (string.IsNullOrWhiteSpace(needle) || !name.Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;
            firstByTitle ??= i;
            if (playing) playingByTitle ??= i;
        }
        if (firstByTitle is not null) return playingByTitle ?? firstByTitle;
        return playingCount == 1 ? singlePlaying : null;
    }

    private static bool IsPlaying(string name)
    {
        foreach (var m in AudioMarkers)
            if (name.Contains(m, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
