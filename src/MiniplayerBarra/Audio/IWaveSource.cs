using MiniplayerBarra.Core;

namespace MiniplayerBarra.Audio;

/// <summary>Fonte das bandas das ondas sonoras. <see cref="Frame"/> é chamado na thread da Ui (~30 quadros/s).</summary>
public interface IWaveSource
{
    void Start(SourceKind kind);
    void Stop();
    float[] Frame();
}
