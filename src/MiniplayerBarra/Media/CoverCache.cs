namespace MiniplayerBarra.Media;

/// <summary>
/// Capa guardada por sessão. Só serve a capa se o título for o mesmo e a sessão não tiver avisado
/// mudança (MediaPropertiesChanged) desde a leitura: o Chrome muda o título antes da miniatura e às
/// vezes manda o ícone do app como miniatura provisória. Não é thread-safe: quem usa trava.
/// </summary>
internal sealed class CoverCache
{
    private sealed record Entry(string Title, byte[]? Cover, bool Stale);

    private readonly Dictionary<string, Entry> _entries = new();

    /// <summary>Muda a cada Invalidate. Quem lê a capa guarda o valor antes e passa para Store.</summary>
    public long Version { get; private set; }

    public bool TryGet(string id, string title, out byte[]? cover)
    {
        cover = null;
        if (!_entries.TryGetValue(id, out var e) || e.Stale || e.Title != title || e.Cover is null) return false;
        cover = e.Cover;
        return true;
    }

    /// <summary>
    /// Guarda o resultado de uma leitura (null = falhou). Se houve Invalidate durante a leitura
    /// (readVersion diferente de Version), a capa fica marcada como velha. Retorna true se o
    /// título é novo para a sessão.
    /// </summary>
    public bool Store(string id, string title, byte[]? cover, long? readVersion = null)
    {
        bool newTitle = !_entries.TryGetValue(id, out var old) || old.Title != title;
        _entries[id] = new Entry(title, cover, readVersion is { } v && v != Version);
        return newTitle;
    }

    /// <summary>Marca como velha a capa de todas as sessões desse AUMID (ids "AUMID" e "AUMID#n").</summary>
    public void Invalidate(string aumid)
    {
        Version++;
        foreach (var (id, e) in _entries.ToList())
        {
            if (id == aumid || id.StartsWith(aumid + "#", StringComparison.Ordinal))
                _entries[id] = e with { Stale = true };
        }
    }

    public void Prune(IReadOnlySet<string> alive)
    {
        foreach (var id in _entries.Keys.Where(k => !alive.Contains(k)).ToList()) _entries.Remove(id);
    }
}
