using MiniplayerBarra.Core;
using Timer = System.Threading.Timer;

namespace MiniplayerBarra.Audio;

/// <summary>
/// Captura o som da fonte mostrada e entrega as 12 bandas das ondas.
/// Com processo conhecido (Chrome, WhatsApp), captura só a árvore desse processo; sem processo, ou se
/// isso falhar ao iniciar, usa o som geral do PC. Se a captura parar com erro, tenta de novo em 1 s,
/// dobrando a espera até 30 s. <see cref="Frame"/> é chamado na thread da Ui; a captura roda em outra.
/// </summary>
public sealed class AudioMeter : IWaveSource, IDisposable
{
    private const int Bands = 12;
    private const long NoDataSilenceMs = 200;
    private static readonly TimeSpan FirstRetry = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaxRetry = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StableRun = TimeSpan.FromSeconds(30);

    private readonly object _gate = new();
    private SourceKind? _kind;                 // null = parado
    private IAudioCapture? _capture;
    private int _generation;                   // muda a cada Start/Stop; invalida tentativas agendadas
    private Timer? _retryTimer;
    private TimeSpan _retryDelay = FirstRetry;
    private long _captureStartedTick;
    private bool _disposed;

    private volatile SpectrumAnalyzer? _analyzer;
    private long _lastDataTick;

    public void Start(SourceKind kind)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_kind == kind) return;
            StopLocked();
            _kind = kind;
            _retryDelay = FirstRetry;
            OpenLocked(tryProcess: true);
        }
    }

    public void Stop()
    {
        lock (_gate) StopLocked();
    }

    public float[] Frame()
    {
        var analyzer = _analyzer;
        if (analyzer is null) return new float[Bands];
        // Loopback não entrega pacotes quando nada toca: sem dados recentes, as barras caem.
        if (Environment.TickCount64 - Interlocked.Read(ref _lastDataTick) > NoDataSilenceMs) analyzer.Silence();
        return analyzer.Frame();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            StopLocked();
        }
    }

    private void StopLocked()
    {
        _generation++;
        _kind = null;
        _analyzer = null;
        _retryTimer?.Dispose();
        _retryTimer = null;
        CloseCaptureLocked();
    }

    private void CloseCaptureLocked()
    {
        var capture = _capture;
        _capture = null;
        capture?.Dispose();
    }

    private void OpenLocked(bool tryProcess)
    {
        var kind = _kind!.Value;
        IAudioCapture? capture = null;
        try
        {
            string? name = SourceProcess.ProcessNameFor(kind);
            if (tryProcess && name is not null)
            {
                int? pid = SourceProcess.FindRootPid(name, SourceProcess.Snapshot());
                if (pid is int root)
                {
                    capture = new ProcessLoopbackCapture(root);
                    Log.Info($"Ondas: capturando o som do processo {name} (pid {root}) e filhos");
                }
                else Log.Info($"Ondas: processo {name} não encontrado; usando o som geral");
            }
            else if (name is null) Log.Info($"Ondas: fonte {kind} sem processo conhecido; usando o som geral");

            if (capture is null)
            {
                capture = new SystemLoopbackCapture();
                Log.Info("Ondas: capturando o som geral do PC");
            }

            var analyzer = _analyzer;
            if (analyzer is null || analyzer.SampleRate != capture.SampleRate)
                analyzer = _analyzer = new SpectrumAnalyzer(capture.SampleRate, Bands);

            var current = capture;
            current.Data += mono =>
            {
                if (!ReferenceEquals(Volatile.Read(ref _capture), current)) return;
                analyzer.Push(mono);
                Interlocked.Exchange(ref _lastDataTick, Environment.TickCount64);
            };
            current.Failed += (ex, wasRunning) =>
                ThreadPool.QueueUserWorkItem(_ => OnFailed(current, ex, wasRunning));

            _capture = current;
            _captureStartedTick = Environment.TickCount64;
            current.Start();
        }
        catch (Exception ex)
        {
            Log.Error("Ondas", ex);
            bool wasProcess = capture is ProcessLoopbackCapture;
            if (ReferenceEquals(_capture, capture)) _capture = null;
            capture?.Dispose();
            if (wasProcess) OpenLocked(tryProcess: false);
            else ScheduleRetryLocked();
        }
    }

    private void OnFailed(IAudioCapture capture, Exception ex, bool wasRunning)
    {
        lock (_gate)
        {
            if (_disposed || !ReferenceEquals(_capture, capture)) return;
            Log.Error("Ondas", ex);
            CloseCaptureLocked();
            if (!wasRunning && capture is ProcessLoopbackCapture)
            {
                Log.Info("Ondas: a captura por processo falhou ao iniciar; usando o som geral");
                OpenLocked(tryProcess: false);
                return;
            }
            if (TimeSpan.FromMilliseconds(Environment.TickCount64 - _captureStartedTick) >= StableRun)
                _retryDelay = FirstRetry;
            ScheduleRetryLocked();
        }
    }

    private void ScheduleRetryLocked()
    {
        var delay = _retryDelay;
        _retryDelay = TimeSpan.FromTicks(Math.Min(MaxRetry.Ticks, delay.Ticks * 2));
        int generation = _generation;
        Log.Info($"Ondas: nova tentativa de captura em {delay.TotalSeconds:0} s");
        _retryTimer?.Dispose();
        _retryTimer = new Timer(_ => Retry(generation), null, delay, Timeout.InfiniteTimeSpan);
    }

    private void Retry(int generation)
    {
        lock (_gate)
        {
            if (_disposed || generation != _generation || _kind is null) return;
            _retryTimer?.Dispose();
            _retryTimer = null;
            OpenLocked(tryProcess: true);
        }
    }
}
