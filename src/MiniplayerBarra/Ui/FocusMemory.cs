namespace MiniplayerBarra.Ui;

/// <summary>
/// Decide para qual janela o FocusKeeper pode devolver o foco, sem tocar em janelas de verdade.
/// Regra: nunca guardar um valor velho. Ao passar o mouse anotamos a janela da frente como
/// candidata (ou nada, se a frente estiver vazia, na barra ou em nós). No botão pressionado, a
/// janela da frente ganha; se ela estiver vazia/na barra/em nós (o Explorer às vezes põe a barra
/// na frente já no botão pressionado), vale a candidata desta passagem do mouse; senão, nada, a
/// não ser que um ciclo de devolução (até 300 ms) ainda esteja em andamento. O fim do ciclo apaga.
/// </summary>
internal sealed class FocusMemory
{
    private IntPtr _candidate;
    private bool _cycle;

    /// <summary>Janela que pode receber o foco de volta (Zero = nenhuma).</summary>
    public IntPtr Saved { get; private set; }

    public void OnHover(IntPtr fg, bool fgEmptyOrOurs) => _candidate = fgEmptyOrOurs ? IntPtr.Zero : fg;

    public void OnLeave() => _candidate = IntPtr.Zero;

    public void OnPress(IntPtr fg, bool fgEmptyOrOurs)
    {
        if (!fgEmptyOrOurs) Saved = fg;
        else if (_candidate != IntPtr.Zero) Saved = _candidate;
        else if (!_cycle) Saved = IntPtr.Zero;
    }

    /// <summary>Clique que não deve devolver o foco (capa/texto): esquece.</summary>
    public void Forget()
    {
        Saved = IntPtr.Zero;
        _cycle = false;
    }

    public void BeginCycle() => _cycle = true;

    public void EndCycle()
    {
        _cycle = false;
        Saved = IntPtr.Zero;
    }

    /// <summary>
    /// Só devolve se a frente estiver vazia, na barra ou em nós, e a janela guardada ainda existir,
    /// estiver visível e não minimizada. Outra janela que veio para a frente é respeitada.
    /// </summary>
    public static bool ShouldRestore(IntPtr saved, bool savedAlive, bool savedMinimized, IntPtr fg, bool fgEmptyOrOurs) =>
        saved != IntPtr.Zero && savedAlive && !savedMinimized && fg != saved && fgEmptyOrOurs;
}
