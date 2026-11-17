namespace MiniplayerBarra.Ui;

/// <summary>Timer dos quadros das ondas. Tick acontece na thread da interface.</summary>
internal interface IFrameTimer : IDisposable
{
    bool Running { get; }
    event Action? Tick;
    void Start();
    void Stop();
}

/// <summary>Timer do WinForms (roda na thread da interface, sem custo enquanto parado).</summary>
internal sealed class WinFormsFrameTimer : IFrameTimer
{
    private readonly System.Windows.Forms.Timer _timer;

    public WinFormsFrameTimer(int intervalMs)
    {
        _timer = new System.Windows.Forms.Timer { Interval = intervalMs };
        _timer.Tick += (_, _) => Tick?.Invoke();
    }

    public bool Running => _timer.Enabled;
    public event Action? Tick;

    public void Start() => _timer.Start();
    public void Stop() => _timer.Stop();
    public void Dispose() => _timer.Dispose();
}
