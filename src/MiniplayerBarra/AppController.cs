using System.Drawing;
using MiniplayerBarra.Audio;
using MiniplayerBarra.Core;
using MiniplayerBarra.Media;
using MiniplayerBarra.Ui;

namespace MiniplayerBarra;

/// <summary>
/// Liga tudo: a cada mudança nas sessões, GetAsync → SessionPicker → view.Show; botões → comandos
/// na sessão mostrada (e um refresh); ondas só enquanto a mídia mostrada toca.
/// Todo o estado vive na thread da interface: eventos de outras threads passam por <c>post</c>.
/// </summary>
internal sealed class AppController : IDisposable
{
    private const int FrameIntervalMs = 33;

    private readonly IMediaSessions _sessions;
    private readonly IPlayerView _view;
    private readonly Settings _settings;
    private readonly Func<Color> _accent;
    private readonly IWaveSource? _waves;
    private readonly Action<Action> _post;
    private readonly IFrameTimer _timer;
    private readonly Func<TimeSpan, Task> _delay;
    private readonly Action<MediaSnapshot> _openSource;
    private readonly Func<byte[]?, Color, (Bitmap? Image, Color WaveColor)> _decode;

    private int _generation;            // só a resposta do pedido mais recente é aplicada
    private MediaSnapshot? _shown;
    private Bitmap? _cover;             // nossa; descartada só depois que a view recebe a próxima
    private Color _waveColor;
    private CoverKey? _coverKey;
    private SourceKind? _wavesKind;     // null = ondas paradas
    private bool _subscribed;
    private bool _disposed;

    /// <param name="post">Executa na thread da interface. Padrão: o SynchronizationContext atual (ou na hora, se não houver).</param>
    public AppController(IMediaSessions sessions, IPlayerView view, Settings settings, Func<Color> accent, IWaveSource? waves,
        Action<Action>? post = null,
        Func<IFrameTimer>? timerFactory = null,
        Func<TimeSpan, Task>? delay = null,
        Action<MediaSnapshot>? openSource = null,
        Func<byte[]?, Color, (Bitmap? Image, Color WaveColor)>? decode = null)
    {
        _sessions = sessions;
        _view = view;
        _settings = settings;
        _accent = accent;
        _waves = waves;
        _post = post ?? PostTo(SynchronizationContext.Current);
        _timer = (timerFactory ?? (() => new WinFormsFrameTimer(FrameIntervalMs)))();
        _delay = delay ?? (t => Task.Delay(t));
        _openSource = openSource ?? (m => SourceWindow.Activate(m));
        _decode = decode ?? CoverArt.Decode;
        _timer.Tick += OnFrame;
    }

    private static Action<Action> PostTo(SynchronizationContext? ui) =>
        ui is null ? a => a() : a => ui.Post(_ => a(), null);

    public Task StartAsync()
    {
        if (!_subscribed)
        {
            _subscribed = true;
            _sessions.Changed += OnChanged;
            _view.Clicked += OnClicked;
            _view.PauseAllRequested += OnPauseAllRequested;
        }
        return RefreshAsync();
    }

    private void OnChanged() => _post(RequestRefresh);

    private void RequestRefresh()
    {
        if (!_disposed) _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        int generation = ++_generation;
        IReadOnlyList<MediaSnapshot> list;
        try
        {
            list = await _sessions.GetAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Error("Sessões de mídia", ex);
            return;
        }
        _post(() => Apply(generation, list));
    }

    private void Apply(int generation, IReadOnlyList<MediaSnapshot> list)
    {
        if (_disposed || generation != _generation) return;
        try
        {
            var media = SessionPicker.Pick(list, _settings.Priority);
            _shown = media;

            Bitmap? old = null;
            if (media is null)
            {
                old = _cover;
                _cover = null;
                _coverKey = null;
            }
            else
            {
                var key = CoverKey.Of(media);
                if (key != _coverKey)
                {
                    old = _cover;
                    (_cover, _waveColor) = DecodeSafely(media.Cover);
                    _coverKey = key;
                }
            }

            try
            {
                _view.Show(media is null ? null : new PlayerState(media, _cover, _waveColor));
            }
            finally
            {
                // A view pode ter guardado a capa anterior até este Show.
                old?.Dispose();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Mostrar mídia", ex);
        }
        UpdateWaves();
    }

    private (Bitmap?, Color) DecodeSafely(byte[]? bytes)
    {
        Color accent;
        try { accent = _accent(); }
        catch (Exception) { accent = Color.FromArgb(0x00, 0x78, 0xD4); }
        try
        {
            return _decode(bytes, accent);
        }
        catch (Exception ex)
        {
            Log.Error("Capa", ex);
            return (null, accent);
        }
    }

    private void UpdateWaves()
    {
        try
        {
            var shown = _shown;
            if (_waves is not null && _settings.Waves && shown is { IsPlaying: true })
            {
                if (_wavesKind != shown.Kind)
                {
                    _waves.Start(shown.Kind);
                    _wavesKind = shown.Kind;
                }
                if (!_timer.Running) _timer.Start();
            }
            else
            {
                _timer.Stop();
                if (_wavesKind is not null)
                {
                    _wavesKind = null;
                    _waves?.Stop();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Ondas", ex);
        }
    }

    private void OnFrame()
    {
        if (_disposed || _waves is null || _wavesKind is null) return;
        try
        {
            _view.SetBands(_waves.Frame());
        }
        catch (Exception ex)
        {
            Log.Error("Quadro das ondas", ex);
        }
    }

    private void OnClicked(PlayerHit hit)
    {
        var media = _shown;
        if (_disposed || media is null) return;
        Func<string, Task<bool>>? command = hit switch
        {
            PlayerHit.PlayPause => _sessions.TogglePlayPauseAsync,
            PlayerHit.Next => _sessions.NextAsync,
            PlayerHit.Previous => _sessions.PreviousAsync,
            _ => null,
        };
        if (command is not null)
        {
            _ = RunCommandAsync(command, media.SessionId, hit);
        }
        else if (hit == PlayerHit.OpenSource)
        {
            try { _openSource(media); }
            catch (Exception ex) { Log.Error("Abrir fonte", ex); }
        }
    }

    private async Task RunCommandAsync(Func<string, Task<bool>> command, string sessionId, PlayerHit hit)
    {
        try
        {
            if (!await command(sessionId).ConfigureAwait(false))
                Log.Info($"Comando {hit} sem efeito: a sessão {sessionId} sumiu ou recusou");
        }
        catch (Exception ex)
        {
            Log.Error($"Comando {hit}", ex);
        }
        _post(RequestRefresh);
    }

    private void OnPauseAllRequested()
    {
        if (!_disposed) _ = PauseAllAsync();
    }

    private async Task PauseAllAsync()
    {
        try
        {
            int n = await PauseAll.RunAsync(_sessions, _delay).ConfigureAwait(false);
            Log.Info($"Pausar tudo: {n} sessão(ões) pausada(s)");
        }
        catch (Exception ex)
        {
            Log.Error("Pausar tudo", ex);
        }
        _post(RequestRefresh);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_subscribed)
        {
            _sessions.Changed -= OnChanged;
            _view.Clicked -= OnClicked;
            _view.PauseAllRequested -= OnPauseAllRequested;
        }
        _timer.Tick -= OnFrame;
        _timer.Dispose();
        try
        {
            if (_wavesKind is not null) _waves?.Stop();
            _view.Show(null); // a view não pode ficar com a capa que vamos descartar
        }
        catch (Exception ex)
        {
            Log.Error("Encerrar", ex);
        }
        _wavesKind = null;
        _cover?.Dispose();
        _cover = null;
    }

    /// <summary>Identifica a capa decodificada: muda com a sessão, o título ou o conteúdo da imagem.</summary>
    private readonly record struct CoverKey(string SessionId, string Title, int Length, int Hash)
    {
        public static CoverKey Of(MediaSnapshot m)
        {
            var bytes = m.Cover;
            int hash = 0;
            if (bytes is { Length: > 0 })
            {
                var h = new HashCode();
                h.AddBytes(bytes);
                hash = h.ToHashCode();
            }
            return new CoverKey(m.SessionId, m.Title ?? "", bytes?.Length ?? -1, hash);
        }
    }
}
