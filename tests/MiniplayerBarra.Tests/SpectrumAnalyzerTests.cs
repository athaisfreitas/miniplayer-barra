using MiniplayerBarra.Audio;

namespace MiniplayerBarra.Tests;

public class SpectrumAnalyzerTests
{
    private const int Rate = 44100;

    private static float[] Sine(double freq, float amplitude, int count = 2048)
    {
        var s = new float[count];
        for (int i = 0; i < count; i++) s[i] = amplitude * (float)Math.Sin(2 * Math.PI * freq * i / Rate);
        return s;
    }

    private static int BandOf(double freq, int bands = 12)
    {
        // Bandas logarítmicas entre 60 Hz e 12 kHz.
        double ratio = Math.Log(freq / 60.0) / Math.Log(12000.0 / 60.0);
        return (int)Math.Floor(ratio * bands);
    }

    [Fact]
    public void Silence_gives_all_zero()
    {
        var a = new SpectrumAnalyzer(Rate);
        a.Push(new float[2048]);
        var frame = a.Frame();
        Assert.Equal(12, frame.Length);
        Assert.All(frame, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Frame_before_any_push_is_all_zero()
    {
        var frame = new SpectrumAnalyzer(Rate).Frame();
        Assert.Equal(12, frame.Length);
        Assert.All(frame, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Sine_1kHz_peaks_in_band_containing_1kHz()
    {
        var a = new SpectrumAnalyzer(Rate);
        a.Push(Sine(1000, 0.5f));
        var frame = a.Frame();

        int expected = BandOf(1000);
        int max = Array.IndexOf(frame, frame.Max());
        Assert.Equal(expected, max);
        Assert.True(frame[expected] > 0.5f, $"banda de 1 kHz = {frame[expected]}");
        for (int b = 0; b < 12; b++)
        {
            double upper = 60.0 * Math.Pow(12000.0 / 60.0, (b + 1) / 12.0);
            if (upper <= 200) Assert.True(frame[b] < 0.2f, $"banda {b} = {frame[b]}");
        }
        Assert.All(frame, v => Assert.InRange(v, 0f, 1f));
    }

    [Fact]
    public void Decay_is_gradual()
    {
        var a = new SpectrumAnalyzer(Rate);
        a.Push(Sine(1000, 0.5f));
        var loud = a.Frame();
        a.Push(new float[2048]);
        var next = a.Frame();

        for (int b = 0; b < 12; b++)
            Assert.True(loud[b] - next[b] <= 0.06f + 1e-5f, $"banda {b}: {loud[b]} -> {next[b]}");
        int peak = BandOf(1000);
        Assert.True(next[peak] < loud[peak], "a banda do tom deve começar a cair");
    }

    [Fact]
    public void Rise_is_immediate()
    {
        var a = new SpectrumAnalyzer(Rate);
        a.Push(new float[2048]);
        a.Frame();
        a.Push(Sine(1000, 0.5f));
        Assert.True(a.Frame()[BandOf(1000)] > 0.5f);
    }

    [Fact]
    public void Fewer_than_2048_samples_are_not_analyzed_yet()
    {
        var a = new SpectrumAnalyzer(Rate);
        a.Push(Sine(1000, 0.5f, 1024));
        Assert.All(a.Frame(), v => Assert.Equal(0f, v));
        a.Push(Sine(1000, 0.5f, 1024));
        Assert.True(a.Frame()[BandOf(1000)] > 0.5f);
    }
}
