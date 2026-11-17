using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniplayerBarra.Core;

/// <summary>Configuração lida de config.json (chaves em português).</summary>
public record Settings(IReadOnlyList<SourceKind> Priority, string YouTubeMusicId, string WhatsAppIdPrefix, bool Waves)
{
    public static Settings Default { get; } = new(
        new[] { SourceKind.YouTubeMusic, SourceKind.ChromeTab, SourceKind.WhatsApp },
        "cinhimbnkkghhklpknlkffjgod",
        "5319275A.WhatsAppDesktop",
        true);

    public static Settings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return Default;
            var raw = JsonSerializer.Deserialize<RawSettings>(File.ReadAllText(path));
            if (raw is null) return Default;
            return new Settings(
                raw.Prioridade is null ? Default.Priority : ParsePriority(raw.Prioridade),
                string.IsNullOrWhiteSpace(raw.IdYoutubeMusic) ? Default.YouTubeMusicId : raw.IdYoutubeMusic.Trim(),
                string.IsNullOrWhiteSpace(raw.IdWhatsApp) ? Default.WhatsAppIdPrefix : raw.IdWhatsApp.Trim(),
                raw.Ondas ?? Default.Waves);
        }
        catch (Exception ex)
        {
            Log.Info($"config.json não pôde ser lido ({ex.GetType().Name}: {ex.Message}); usando a configuração padrão");
            return Default;
        }
    }

    private static IReadOnlyList<SourceKind> ParsePriority(IEnumerable<string?> names)
    {
        var result = new List<SourceKind>();
        foreach (var name in names)
        {
            SourceKind? kind = name?.Trim().ToLowerInvariant() switch
            {
                "youtubemusic" => SourceKind.YouTubeMusic,
                "chrome" => SourceKind.ChromeTab,
                "whatsapp" => SourceKind.WhatsApp,
                _ => null,
            };
            if (kind is { } k && !result.Contains(k)) result.Add(k);
        }
        return result;
    }

    private sealed class RawSettings
    {
        [JsonPropertyName("prioridade")] public string?[]? Prioridade { get; set; }
        [JsonPropertyName("idYoutubeMusic")] public string? IdYoutubeMusic { get; set; }
        [JsonPropertyName("idWhatsApp")] public string? IdWhatsApp { get; set; }
        [JsonPropertyName("ondas")] public bool? Ondas { get; set; }
    }
}
