using NAudio.Dsp;

namespace MiniplayerBarra.Audio;

/// <summary>
/// Transforma o som (mono) em bandas de 0 a 1 para as barrinhas.
/// FFT de 2048 amostras com janela Hann, bandas logarítmicas entre 60 Hz e 12 kHz,
/// nível em dB normalizado entre −60 dB (0) e −10 dB (1).
/// <see cref="Push"/> roda na thread de captura e <see cref="Frame"/> na thread da Ui.
/// </summary>
public sealed class SpectrumAnalyzer
{
    private const int FftSize = 2048;
    private const int FftLog2 = 11;
    private const double LowHz = 60, HighHz = 12000;
    private const double FloorDb = -60, CeilDb = -10;
    private const float MaxFallPerFrame = 0.06f;

    private readonly int _bands;
    private readonly int _hop;               // amostras novas entre duas análises (~33 ms de som)
    private readonly int[] _bandFirstBin, _bandLastBin;
    private readonly float[] _window = new float[FftSize];

    // Só a thread de captura mexe nestes.
    private readonly float[] _ring = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly float[] _levels;
    private int _ringPos, _filled, _sinceAnalysis;
    private volatile bool _resetPending;

    // Compartilhados entre as threads, sob _gate.
    private readonly object _gate = new();
    private readonly float[] _target, _shown;

    public SpectrumAnalyzer(int sampleRate, int bands = 12)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (bands <= 0) throw new ArgumentOutOfRangeException(nameof(bands));
        SampleRate = sampleRate;
        _bands = bands;
        _hop = Math.Max(1, sampleRate * 33 / 1000);
        _levels = new float[bands];
        _target = new float[bands];
        _shown = new float[bands];

        for (int i = 0; i < FftSize; i++) _window[i] = (float)FastFourierTransform.HannWindow(i, FftSize);

        double binHz = (double)sampleRate / FftSize;
        int lastUsable = FftSize / 2 - 1;
        _bandFirstBin = new int[bands];
        _bandLastBin = new int[bands];
        for (int b = 0; b < bands; b++)
        {
            double lo = LowHz * Math.Pow(HighHz / LowHz, (double)b / bands);
            double hi = LowHz * Math.Pow(HighHz / LowHz, (double)(b + 1) / bands);
            int first = Math.Clamp((int)Math.Ceiling(lo / binHz), 1, lastUsable);
            int last = Math.Clamp((int)Math.Ceiling(hi / binHz) - 1, first, lastUsable);
            _bandFirstBin[b] = first;
            _bandLastBin[b] = last;
        }
    }

    public int SampleRate { get; }

    /// <summary>Recebe amostras mono (−1..1). Analisa quando há 2048 amostras e passaram ~33 ms de som desde a última análise.</summary>
    public void Push(ReadOnlySpan<float> mono)
    {
        if (_resetPending)
        {
            _resetPending = false;
            _filled = 0;
            _sinceAnalysis = 0;
        }
        if (mono.Length > FftSize) mono = mono[^FftSize..];
        foreach (var s in mono)
        {
            _ring[_ringPos] = s;
            _ringPos = (_ringPos + 1) % FftSize;
        }
        _filled = Math.Min(FftSize, _filled + mono.Length);
        _sinceAnalysis += mono.Length;
        if (_filled < FftSize || _sinceAnalysis < _hop) return;
        _sinceAnalysis = 0;
        Analyze();
    }

    /// <summary>Próximo quadro da animação: sobe na hora e desce no máximo 0,06 por quadro.</summary>
    public float[] Frame()
    {
        var result = new float[_bands];
        lock (_gate)
        {
            for (int b = 0; b < _bands; b++)
            {
                float t = _target[b];
                _shown[b] = t >= _shown[b] ? t : Math.Max(t, _shown[b] - MaxFallPerFrame);
                result[b] = _shown[b];
            }
        }
        return result;
    }

    /// <summary>
    /// Sem som chegando (qualquer thread): zera o alvo, para as barras caírem aos poucos,
    /// e o histórico é descartado no próximo <see cref="Push"/>.
    /// </summary>
    public void Silence()
    {
        lock (_gate) Array.Clear(_target);
        _resetPending = true;
    }

    private void Analyze()
    {
        // _ringPos aponta para a amostra mais antiga.
        for (int i = 0; i < FftSize; i++)
        {
            _fft[i].X = _ring[(_ringPos + i) % FftSize] * _window[i];
            _fft[i].Y = 0;
        }
        FastFourierTransform.FFT(true, FftLog2, _fft);   // a forward do NAudio já divide por N

        for (int b = 0; b < _bands; b++)
        {
            double power = 0;
            for (int k = _bandFirstBin[b]; k <= _bandLastBin[b]; k++)
                power += _fft[k].X * _fft[k].X + _fft[k].Y * _fft[k].Y;
            // ×2 (lado único do espectro) e ÷0,5 (ganho da janela Hann) → amplitude de um seno.
            double amplitude = 4 * Math.Sqrt(power);
            double db = 20 * Math.Log10(amplitude + 1e-12);
            _levels[b] = (float)Math.Clamp((db - FloorDb) / (CeilDb - FloorDb), 0, 1);
        }

        lock (_gate) Array.Copy(_levels, _target, _bands);
    }
}
