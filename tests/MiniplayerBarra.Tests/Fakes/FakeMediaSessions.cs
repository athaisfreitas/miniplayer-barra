using MiniplayerBarra.Core;
using MiniplayerBarra.Media;

namespace MiniplayerBarra.Tests.Fakes;

public class FakeMediaSessions : IMediaSessions
{
    public List<MediaSnapshot> Sessions { get; } = new();
    public List<string> Calls { get; } = new();

    /// <summary>Quando a sessão com este id é parada, aparece uma nova sessão tocando (até Remaining vezes).</summary>
    public string? SpawnOnStopOf { get; set; }
    public int SpawnRemaining { get; set; }

    /// <summary>Sessão que volta a tocar sempre que é pausada.</summary>
    public string? ResumeOnPauseOf { get; set; }

    /// <summary>Se true, GetAsync fica pendente até o teste completar o item em PendingGets.</summary>
    public bool DeferGets { get; set; }
    public List<(TaskCompletionSource<IReadOnlyList<MediaSnapshot>> Reply, List<MediaSnapshot> Snapshot)> PendingGets { get; } = new();

    public event Action? Changed;

    public void RaiseChanged() => Changed?.Invoke();

    public Task<IReadOnlyList<MediaSnapshot>> GetAsync()
    {
        var snapshot = Sessions.ToList();
        if (!DeferGets) return Task.FromResult<IReadOnlyList<MediaSnapshot>>(snapshot);
        var reply = new TaskCompletionSource<IReadOnlyList<MediaSnapshot>>();
        PendingGets.Add((reply, snapshot));
        return reply.Task;
    }

    /// <summary>Responde o GetAsync pendente de índice i com a lista do momento da chamada.</summary>
    public void CompleteGet(int i) => PendingGets[i].Reply.SetResult(PendingGets[i].Snapshot);

    public Task<bool> PauseAsync(string sessionId)
    {
        Calls.Add("pause:" + sessionId);
        var i = Sessions.FindIndex(s => s.SessionId == sessionId);
        if (i < 0) return Task.FromResult(false);
        if (sessionId != ResumeOnPauseOf) Sessions[i] = Sessions[i] with { IsPlaying = false };
        return Task.FromResult(true);
    }

    public Task<bool> StopAsync(string sessionId)
    {
        Calls.Add("stop:" + sessionId);
        var i = Sessions.FindIndex(s => s.SessionId == sessionId);
        if (i < 0) return Task.FromResult(false);
        if (sessionId == SpawnOnStopOf && SpawnRemaining > 0)
        {
            SpawnRemaining--;
            Sessions[i] = Sessions[i] with { IsPlaying = true };
        }
        return Task.FromResult(true);
    }

    public Task<bool> TogglePlayPauseAsync(string sessionId) => Command("toggle", sessionId);
    public Task<bool> NextAsync(string sessionId) => Command("next", sessionId);
    public Task<bool> PreviousAsync(string sessionId) => Command("previous", sessionId);

    private Task<bool> Command(string name, string sessionId)
    {
        Calls.Add(name + ":" + sessionId);
        return Task.FromResult(Sessions.Any(s => s.SessionId == sessionId));
    }
    public void Dispose() { }
}
