using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MiniplayerBarra.Audio;

internal delegate void MonoDataHandler(ReadOnlySpan<float> mono);

/// <summary>Uma captura de som que entrega amostras mono. Dispose para a captura e libera tudo.</summary>
internal interface IAudioCapture : IDisposable
{
    int SampleRate { get; }

    /// <summary>Thread de captura.</summary>
    event MonoDataHandler? Data;

    /// <summary>A captura parou com erro. O bool diz se ela chegou a rodar (false = falhou ao iniciar).</summary>
    event Action<Exception, bool>? Failed;

    void Start();
}

/// <summary>Converte PCM intercalado (float 32, inteiro 16/24/32) em mono float, reaproveitando o buffer.</summary>
internal sealed class MonoConverter
{
    private static readonly Guid SubtypePcm = new("00000001-0000-0010-8000-00aa00389b71");
    private static readonly Guid SubtypeFloat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly int _channels, _bits, _blockAlign;
    private readonly bool _isFloat;
    private float[] _buffer = Array.Empty<float>();

    public MonoConverter(WaveFormat format)
    {
        _channels = format.Channels;
        _bits = format.BitsPerSample;
        _blockAlign = format.BlockAlign;
        var encoding = format.Encoding;
        if (encoding == WaveFormatEncoding.Extensible && format is WaveFormatExtensible ext)
            encoding = ext.SubFormat == SubtypeFloat ? WaveFormatEncoding.IeeeFloat
                     : ext.SubFormat == SubtypePcm ? WaveFormatEncoding.Pcm : encoding;
        _isFloat = encoding == WaveFormatEncoding.IeeeFloat && _bits == 32;
        bool pcm = encoding == WaveFormatEncoding.Pcm && (_bits is 16 or 24 or 32);
        if (_channels < 1 || !(_isFloat || pcm))
            throw new NotSupportedException($"Formato de áudio não suportado: {format}");
    }

    public ReadOnlySpan<float> Convert(ReadOnlySpan<byte> data)
    {
        int frames = data.Length / _blockAlign;
        if (_buffer.Length < frames) _buffer = new float[Math.Max(frames, 4096)];
        float scale = 1f / _channels;
        for (int f = 0; f < frames; f++)
        {
            var frame = data.Slice(f * _blockAlign, _blockAlign);
            float sum = 0;
            for (int c = 0; c < _channels; c++) sum += Sample(frame, c);
            _buffer[f] = sum * scale;
        }
        return _buffer.AsSpan(0, frames);
    }

    public ReadOnlySpan<float> Zeros(int frames)
    {
        if (_buffer.Length < frames) _buffer = new float[Math.Max(frames, 4096)];
        Array.Clear(_buffer, 0, frames);
        return _buffer.AsSpan(0, frames);
    }

    private float Sample(ReadOnlySpan<byte> frame, int channel)
    {
        if (_isFloat) return MemoryMarshal.Read<float>(frame[(channel * 4)..]);
        switch (_bits)
        {
            case 16: return MemoryMarshal.Read<short>(frame[(channel * 2)..]) / 32768f;
            case 24:
                var b = frame[(channel * 3)..];
                return ((b[0] << 8 | b[1] << 16 | b[2] << 24) >> 8) / 8388608f;
            default: return MemoryMarshal.Read<int>(frame[(channel * 4)..]) / 2147483648f;
        }
    }
}

/// <summary>Plano B: som geral do PC (loopback do dispositivo de saída padrão).</summary>
internal sealed class SystemLoopbackCapture : IAudioCapture
{
    private readonly WasapiLoopbackCapture _capture;
    private readonly MonoConverter _converter;
    private volatile bool _disposed;

    public SystemLoopbackCapture()
    {
        _capture = new WasapiLoopbackCapture();
        try
        {
            _converter = new MonoConverter(_capture.WaveFormat);
        }
        catch
        {
            _capture.Dispose();
            throw;
        }
        SampleRate = _capture.WaveFormat.SampleRate;
        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnStopped;
    }

    public int SampleRate { get; }
    public event MonoDataHandler? Data;
    public event Action<Exception, bool>? Failed;

    public void Start() => _capture.StartRecording();

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (_disposed) return;
        Data?.Invoke(_converter.Convert(e.Buffer.AsSpan(0, e.BytesRecorded)));
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (_disposed) return;
        Failed?.Invoke(e.Exception ?? new InvalidOperationException("A captura do som geral parou sozinha."), true);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _capture.DataAvailable -= OnData;
        _capture.RecordingStopped -= OnStopped;
        _capture.Dispose();
    }
}
