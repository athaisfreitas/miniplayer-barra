using MiniplayerBarra.Core;

namespace MiniplayerBarra.Ui;

/// <summary>
/// Clicar numa janela filha da barra de tarefas às vezes deixa o Windows sem janela em primeiro
/// plano (GetForegroundWindow() == 0), e a próxima tecla se perde, mesmo com WS_EX_NOACTIVATE +
/// MA_NOACTIVATE; às vezes o Explorer põe a própria barra na frente já no botão pressionado. Ao
/// passar o mouse e no botão pressionado anotamos a janela da frente (regras em FocusMemory: nunca
/// um valor velho, nunca uma janela minimizada); depois do clique (na hora, 100 ms e 300 ms depois)
/// devolvemos o foco a ela se a frente ficou vazia, na barra ou em nós, e então esquecemos.
/// Acabamos de receber entrada do usuário, então o Windows permite o SetForegroundWindow.
/// </summary>
internal sealed class FocusKeeper : IDisposable
{
    private static readonly int[] RetryDelaysMs = { 100, 200 }; // 100 ms e 300 ms depois do clique
    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly FocusMemory _memory = new();
    private int _retry;

    public FocusKeeper()
    {
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>Mouse passando sobre o player: anota a janela da frente como candidata.</summary>
    public void Hover() => Read((fg, empty) => _memory.OnHover(fg, empty));

    /// <summary>Mouse saiu do player: a candidata deixa de valer.</summary>
    public void Leave() => _memory.OnLeave();

    /// <summary>Botão pressionado: decide qual janela pode receber o foco de volta.</summary>
    public void Press() => Read((fg, empty) => _memory.OnPress(fg, empty));

    /// <summary>Clique que traz outra janela de propósito (capa/texto): não devolve nada.</summary>
    public void Forget()
    {
        _timer.Stop();
        _memory.Forget();
    }

    /// <summary>Devolve o foco agora e confere de novo daqui a 100 ms e 300 ms; depois esquece.</summary>
    public void RestoreSoon()
    {
        _memory.BeginCycle();
        Restore();
        _retry = 0;
        _timer.Stop();
        _timer.Interval = RetryDelaysMs[0];
        _timer.Start();
    }

    private void OnTick()
    {
        Restore();
        _retry++;
        if (_retry >= RetryDelaysMs.Length)
        {
            _timer.Stop();
            _memory.EndCycle();
            return;
        }
        _timer.Interval = RetryDelaysMs[_retry];
    }

    private static void Read(Action<IntPtr, bool> use)
    {
        try
        {
            var fg = Native.GetForegroundWindow();
            use(fg, IsEmptyOrOurs(fg));
        }
        catch (Exception ex) { Log.Error("Foco guardar", ex); }
    }

    private void Restore()
    {
        try
        {
            var saved = _memory.Saved;
            if (saved == IntPtr.Zero) return;
            var fg = Native.GetForegroundWindow();
            bool alive = Native.IsWindow(saved) && Native.IsWindowVisible(saved);
            if (FocusMemory.ShouldRestore(saved, alive, Native.IsIconic(saved), fg, IsEmptyOrOurs(fg)))
                Native.SetForegroundWindow(saved);
        }
        catch (Exception ex) { Log.Error("Foco devolver", ex); }
    }

    /// <summary>Sem janela na frente, a barra de tarefas (ou filha dela) ou uma janela deste processo (menu).</summary>
    private static bool IsEmptyOrOurs(IntPtr fg)
    {
        if (fg == IntPtr.Zero) return true;
        var tray = Native.FindWindow("Shell_TrayWnd", null);
        if (fg == tray || Native.GetAncestor(fg, Native.GA_ROOT) == tray) return true;
        Native.GetWindowThreadProcessId(fg, out var pid);
        return pid == OwnPid;
    }

    public void Dispose() => _timer.Dispose();
}
