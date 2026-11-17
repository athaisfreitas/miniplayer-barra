namespace MiniplayerBarra.Core;

/// <summary>Foto de uma sessão de mídia. SessionId = AUMID (com #n se repetir).</summary>
public record MediaSnapshot(string SessionId, SourceKind Kind, string Title, string Artist, bool IsPlaying, byte[]? Cover);
