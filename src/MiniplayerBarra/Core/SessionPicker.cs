namespace MiniplayerBarra.Core;

public static class SessionPicker
{
    /// <summary>Tocando antes de pausado; depois prioridade; fora da lista depois dos listados; Other por último.</summary>
    public static MediaSnapshot? Pick(IReadOnlyList<MediaSnapshot> sessions, IReadOnlyList<SourceKind> priority)
    {
        MediaSnapshot? best = null;
        (int, int) bestKey = default;
        foreach (var s in sessions)
        {
            var key = (s.IsPlaying ? 0 : 1, Rank(s.Kind, priority));
            if (best is null || key.CompareTo(bestKey) < 0)
            {
                best = s;
                bestKey = key;
            }
        }
        return best;
    }

    private static int Rank(SourceKind kind, IReadOnlyList<SourceKind> priority)
    {
        if (kind == SourceKind.Other) return int.MaxValue;
        for (var i = 0; i < priority.Count; i++)
            if (priority[i] == kind) return i;
        return priority.Count;
    }
}
