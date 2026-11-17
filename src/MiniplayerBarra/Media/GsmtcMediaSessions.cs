using MiniplayerBarra.Core;
using Windows.Foundation;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MiniplayerBarra.Media;

/// <summary>Sessões de mídia do Windows (GSMTC). Os eventos chegam em threads do sistema.</summary>
public sealed class GsmtcMediaSessions : IMediaSessions
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);
    private const ulong MaxCoverBytes = 2 * 1024 * 1024;
    // O Chrome pode publicar a miniatura certa depois do título, sem outro aviso: relê uma vez.
    private static readonly TimeSpan CoverRecheckDelay = TimeSpan.FromSeconds(2);

    private readonly Settings _settings;
    private readonly GlobalSystemMediaTransportControlsSessionManager _manager;
    private readonly object _gate = new();
    private readonly List<Subscription> _subs = new();
    private readonly CoverCache _covers = new();
    private bool _disposed;

    public event Action? Changed;

    private sealed record Subscription(
        GlobalSystemMediaTransportControlsSession Session,
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs> Props,
        TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs> Playback);

    private GsmtcMediaSessions(Settings settings, GlobalSystemMediaTransportControlsSessionManager manager)
    {
        _settings = settings;
        _manager = manager;
    }

    public static async Task<GsmtcMediaSessions> CreateAsync(Settings settings)
    {
        var manager = await CallLimit.Run(GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(), CallTimeout);
        var self = new GsmtcMediaSessions(settings, manager);
        manager.SessionsChanged += self.OnSessionsChanged;
        self.Resubscribe();
        return self;
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args)
    {
        Resubscribe();
        Changed?.Invoke();
    }

    private void Resubscribe()
    {
        lock (_gate)
        {
            if (_disposed) return;
            Unsubscribe();
            try
            {
                foreach (var session in _manager.GetSessions())
                {
                    var aumid = session.SourceAppUserModelId;
                    var props = new TypedEventHandler<GlobalSystemMediaTransportControlsSession, MediaPropertiesChangedEventArgs>((_, _) =>
                    {
                        lock (_gate) _covers.Invalidate(aumid);
                        Changed?.Invoke();
                    });
                    var playback = new TypedEventHandler<GlobalSystemMediaTransportControlsSession, PlaybackInfoChangedEventArgs>((_, _) => Changed?.Invoke());
                    session.MediaPropertiesChanged += props;
                    session.PlaybackInfoChanged += playback;
                    _subs.Add(new Subscription(session, props, playback));
                }
            }
            catch (Exception ex) { Log.Error("GsmtcMediaSessions.Resubscribe", ex); }
        }
    }

    private void Unsubscribe()
    {
        foreach (var s in _subs)
        {
            try
            {
                s.Session.MediaPropertiesChanged -= s.Props;
                s.Session.PlaybackInfoChanged -= s.Playback;
            }
            catch (Exception ex) { Log.Error("GsmtcMediaSessions.Unsubscribe", ex); }
        }
        _subs.Clear();
    }

    /// <summary>Sessões atuais com seus ids: AUMID, e AUMID#2, #3 se repetir.</summary>
    private List<(string Id, GlobalSystemMediaTransportControlsSession Session)> Enumerate()
    {
        var result = new List<(string, GlobalSystemMediaTransportControlsSession)>();
        var counts = new Dictionary<string, int>();
        foreach (var session in _manager.GetSessions())
        {
            var aumid = session.SourceAppUserModelId;
            var n = counts.GetValueOrDefault(aumid) + 1;
            counts[aumid] = n;
            result.Add((n == 1 ? aumid : $"{aumid}#{n}", session));
        }
        return result;
    }

    public async Task<IReadOnlyList<MediaSnapshot>> GetAsync()
    {
        var list = new List<MediaSnapshot>();
        List<(string Id, GlobalSystemMediaTransportControlsSession Session)> sessions;
        try { sessions = Enumerate(); }
        catch (Exception ex) { Log.Error("GsmtcMediaSessions.GetAsync", ex); return list; }

        var alive = new HashSet<string>();
        foreach (var (id, session) in sessions)
        {
            alive.Add(id);
            try
            {
                var aumid = session.SourceAppUserModelId;
                var props = await CallLimit.Run(session.TryGetMediaPropertiesAsync().AsTask(), CallTimeout);
                var playing = session.GetPlaybackInfo().PlaybackStatus ==
                              GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                var cover = await GetCoverAsync(id, aumid, props);
                list.Add(new MediaSnapshot(id, SourceClassifier.Classify(aumid, _settings),
                    props.Title ?? "", props.Artist ?? "", playing, cover));
            }
            catch (Exception ex) { Log.Error("GsmtcMediaSessions.GetAsync", ex); }
        }
        lock (_gate)
        {
            _covers.Prune(alive);
        }
        return list;
    }

    private async Task<byte[]?> GetCoverAsync(string id, string aumid, GlobalSystemMediaTransportControlsSessionMediaProperties props)
    {
        var title = props.Title ?? "";
        long version;
        lock (_gate)
        {
            if (_covers.TryGet(id, title, out var cached)) return cached;
            version = _covers.Version;
        }
        byte[]? cover = null;
        try
        {
            var thumb = props.Thumbnail;
            if (thumb is not null)
            {
                using var stream = await CallLimit.Run(thumb.OpenReadAsync().AsTask(), CallTimeout);
                if (stream.Size > 0 && stream.Size <= MaxCoverBytes)
                {
                    var size = (uint)stream.Size;
                    var buffer = new Windows.Storage.Streams.Buffer(size);
                    await CallLimit.Run(stream.ReadAsync(buffer, size, InputStreamOptions.None).AsTask(), CallTimeout);
                    using var reader = DataReader.FromBuffer(buffer);
                    cover = new byte[buffer.Length];
                    reader.ReadBytes(cover);
                }
            }
        }
        catch (Exception ex) { Log.Error("GsmtcMediaSessions.Cover", ex); }
        // Falha não é servida do cache (o app costuma publicar o título antes da capa).
        bool newTitle;
        lock (_gate) newTitle = _covers.Store(id, title, cover, version);
        if (newTitle) _ = RecheckCoverLaterAsync(aumid);
        return cover;
    }

    private async Task RecheckCoverLaterAsync(string aumid)
    {
        try
        {
            await Task.Delay(CoverRecheckDelay).ConfigureAwait(false);
            lock (_gate)
            {
                if (_disposed) return;
                _covers.Invalidate(aumid);
            }
            Changed?.Invoke();
        }
        catch (Exception ex) { Log.Error("GsmtcMediaSessions.RecheckCover", ex); }
    }

    private async Task<bool> RunAsync(string sessionId, string name,
        Func<GlobalSystemMediaTransportControlsSession, IAsyncOperation<bool>> command)
    {
        try
        {
            var found = Enumerate().FirstOrDefault(x => x.Id == sessionId);
            if (found.Session is null) return false;
            return await CallLimit.Run(command(found.Session).AsTask(), CallTimeout);
        }
        catch (Exception ex)
        {
            Log.Error("GsmtcMediaSessions." + name, ex);
            return false;
        }
    }

    public Task<bool> TogglePlayPauseAsync(string sessionId) => RunAsync(sessionId, nameof(TogglePlayPauseAsync), s => s.TryTogglePlayPauseAsync());
    public Task<bool> NextAsync(string sessionId) => RunAsync(sessionId, nameof(NextAsync), s => s.TrySkipNextAsync());
    public Task<bool> PreviousAsync(string sessionId) => RunAsync(sessionId, nameof(PreviousAsync), s => s.TrySkipPreviousAsync());
    public Task<bool> PauseAsync(string sessionId) => RunAsync(sessionId, nameof(PauseAsync), s => s.TryPauseAsync());
    public Task<bool> StopAsync(string sessionId) => RunAsync(sessionId, nameof(StopAsync), s => s.TryStopAsync());

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            Unsubscribe();
        }
        try { _manager.SessionsChanged -= OnSessionsChanged; }
        catch (Exception ex) { Log.Error("GsmtcMediaSessions.Dispose", ex); }
    }
}

/// <summary>Limite de tempo para chamadas WinRT que podem travar se o app de origem não responder.</summary>
internal static class CallLimit
{
    public static async Task<T> Run<T>(Task<T> task, TimeSpan limit)
    {
        try { return await task.WaitAsync(limit); }
        catch (TimeoutException) { throw new TimeoutException($"Sem resposta em {limit.TotalSeconds:0.#} s."); }
    }
}
