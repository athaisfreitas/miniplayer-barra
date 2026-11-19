using System.Runtime.InteropServices;

namespace MiniplayerBarra.Ui;

/// <summary>
/// Hook de mouse de baixo nível, ligado só enquanto o mouse está sobre o player: a barra de tarefas
/// é de outro processo e o Windows não entrega a rodinha à nossa janela filha. Engole o evento
/// (a barra não rola) e repassa o delta, já fora do callback, para <paramref name="onDelta"/>.
/// </summary>
internal sealed class WheelHook : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_MOUSEWHEEL = 0x020A;

    private readonly Func<Native.RECT?> _bounds;   // retângulo do player na tela
    private readonly Action<int> _onDelta;         // chamado na thread da interface
    private readonly SynchronizationContext _ui;
    private readonly HookProc _proc;               // guardado: o GC não pode coletar o delegate
    private IntPtr _hook;

    public WheelHook(Func<Native.RECT?> bounds, Action<int> onDelta)
    {
        _bounds = bounds;
        _onDelta = onDelta;
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();
        _proc = Callback;
    }

    public bool Installed => _hook != IntPtr.Zero;

    public void Install()
    {
        if (_hook != IntPtr.Zero) return;
        _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(null), 0);
    }

    public void Uninstall()
    {
        if (_hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
    }

    private IntPtr Callback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && wParam == (IntPtr)WM_MOUSEWHEEL)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var r = _bounds();
            if (r is { } b && data.pt.X >= b.Left && data.pt.X < b.Right && data.pt.Y >= b.Top && data.pt.Y < b.Bottom)
            {
                int delta = (short)((data.mouseData >> 16) & 0xFFFF);
                _ui.Post(_ => _onDelta(delta), null);
                return (IntPtr)1; // engole
            }
        }
        return CallNextHookEx(_hook, code, wParam, lParam);
    }

    public void Dispose() => Uninstall();

    [StructLayout(LayoutKind.Sequential)]
    private struct MSLLHOOKSTRUCT
    {
        public Native.POINT pt;
        public uint mouseData, flags, time;
        public IntPtr dwExtraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
