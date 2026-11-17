using MiniplayerBarra.Media;

namespace MiniplayerBarra.Core;

public static class PauseAll
{
    private static readonly TimeSpan RoundDelay = TimeSpan.FromMilliseconds(400);

    /// <summary>
    /// Pausa tudo que está tocando. Pausar só não basta no Chrome: ele mantém uma única sessão e
    /// só passa para a próxima aba tocando depois de um Stop, então repete em rodadas.
    /// Devolve quantas sessões foram pausadas.
    /// </summary>
    public static async Task<int> RunAsync(IMediaSessions sessions, Func<TimeSpan, Task> delay, int maxRounds = 10)
    {
        var paused = 0;
        for (var round = 0; round < maxRounds; round++)
        {
            var playing = (await sessions.GetAsync()).Where(s => s.IsPlaying).ToList();
            if (playing.Count == 0) break;
            foreach (var s in playing)
            {
                await sessions.PauseAsync(s.SessionId);
                paused++;
                if (s.Kind == SourceKind.ChromeTab) await sessions.StopAsync(s.SessionId);
            }
            await delay(RoundDelay);
        }
        return paused;
    }
}
