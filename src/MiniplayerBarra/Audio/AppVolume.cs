using System.Diagnostics;
using MiniplayerBarra.Core;
using NAudio.CoreAudioApi;

namespace MiniplayerBarra.Audio;

/// <summary>Volume do app da fonte no Mixer de Volume do Windows (não mexe no volume geral).</summary>
public interface IAppVolume
{
    /// <summary>Volume atual (0..1) do app desse tipo de fonte, ou null se não há sessão de áudio dele.</summary>
    float? Get(SourceKind kind);

    /// <summary>Aplica em todas as sessões de áudio do app. False se não achou nenhuma.</summary>
    bool Set(SourceKind kind, float volume);
}

/// <summary>
/// Usa as sessões de áudio do dispositivo de saída padrão. O Chrome tem uma sessão só para todas
/// as abas e o app do YT Music, então o volume vale para todos eles juntos.
/// </summary>
internal sealed class AppVolume : IAppVolume
{
    public float? Get(SourceKind kind)
    {
        float? max = null;
        ForEachSession(kind, v => max = Math.Max(max ?? 0f, v.Volume));
        return max;
    }

    public bool Set(SourceKind kind, float volume)
    {
        int n = 0;
        ForEachSession(kind, v => { v.Volume = volume; n++; });
        return n > 0;
    }

    private static void ForEachSession(SourceKind kind, Action<SimpleAudioVolume> action)
    {
        var name = SourceProcess.ProcessNameFor(kind);
        if (name is null) return;
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var sessions = manager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.IsSystemSoundsSession) continue;
            string exe;
            try { using var p = Process.GetProcessById((int)s.GetProcessID); exe = p.ProcessName; }
            catch (Exception) { continue; }
            if (SourceProcess.SameName(exe, name)) action(s.SimpleAudioVolume);
        }
    }
}
