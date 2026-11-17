using MiniplayerBarra.Core;

namespace MiniplayerBarra.Media;

public interface IMediaSessions : IDisposable
{
    /// <summary>Pode disparar em qualquer thread.</summary>
    event Action? Changed;

    Task<IReadOnlyList<MediaSnapshot>> GetAsync();

    /// <summary>Os comandos devolvem false se a sessão sumiu ou o comando falhou.</summary>
    Task<bool> TogglePlayPauseAsync(string sessionId);
    Task<bool> NextAsync(string sessionId);
    Task<bool> PreviousAsync(string sessionId);
    Task<bool> PauseAsync(string sessionId);
    Task<bool> StopAsync(string sessionId);
}
