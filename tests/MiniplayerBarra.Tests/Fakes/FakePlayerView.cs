using MiniplayerBarra.Audio;
using MiniplayerBarra.Core;
using MiniplayerBarra.Ui;

namespace MiniplayerBarra.Tests.Fakes;

internal sealed class FakePlayerView : IPlayerView
{
    public List<PlayerState?> Shows { get; } = new();
    public List<float[]> Bands { get; } = new();

    /// <summary>Executado dentro de Show (para conferir o estado no momento da chamada).</summary>
    public Action<PlayerState?>? OnShow { get; set; }

    public PlayerState? Last => Shows.Count == 0 ? null : Shows[^1];

    public event Action<PlayerHit>? Clicked;
    public event Action? PauseAllRequested;
    public event Action? ExitRequested;

    public void Show(PlayerState? state)
    {
        Shows.Add(state);
        OnShow?.Invoke(state);
    }

    public void SetBands(float[] bands) => Bands.Add(bands);

    public void Click(PlayerHit hit) => Clicked?.Invoke(hit);
    public void RequestPauseAll() => PauseAllRequested?.Invoke();
    public void RequestExit() => ExitRequested?.Invoke();

    public void Dispose() { }
}

internal sealed class FakeWaveSource : IWaveSource
{
    public List<string> Calls { get; } = new();

    public void Start(SourceKind kind) => Calls.Add("start:" + kind);
    public void Stop() => Calls.Add("stop");
    public float[] Frame() => new float[] { 0.5f };
}

internal sealed class FakeFrameTimer : IFrameTimer
{
    public bool Running { get; private set; }
    public event Action? Tick;

    public void Start() => Running = true;
    public void Stop() => Running = false;
    public void Fire() => Tick?.Invoke();
    public void Dispose() => Running = false;
}
