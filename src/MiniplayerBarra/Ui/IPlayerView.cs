using System.Drawing;
using MiniplayerBarra.Core;

namespace MiniplayerBarra.Ui;

/// <summary>O que o player mostra. Cover pertence a quem chama (não é descartada aqui).</summary>
internal record PlayerState(MediaSnapshot Media, Bitmap? Cover, Color WaveColor);

/// <summary>O player na tela. Todos os métodos são chamados na thread da interface.</summary>
internal interface IPlayerView : IDisposable
{
    /// <summary>Mostra o estado; null esconde o player.</summary>
    void Show(PlayerState? state);

    /// <summary>Novas alturas das ondas (0..1). Só redesenha.</summary>
    void SetBands(float[] bands);

    event Action<PlayerHit>? Clicked;
    event Action? PauseAllRequested;
    event Action? ExitRequested;
}
