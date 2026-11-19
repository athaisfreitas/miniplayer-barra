using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using MiniplayerBarra.Core;

namespace MiniplayerBarra.Ui;

/// <summary>
/// "Abrir fonte": traz para a frente a janela de nível superior visível da fonte da mídia.
/// Se o título da janela não bater (aba em segundo plano), procura a aba pela UI Automation e a seleciona.
/// </summary>
internal static class SourceWindow
{
    /// <summary>
    /// YT Music: título contém "YouTube Music". Aba do Chrome: processo chrome cujo título contém o
    /// título da mídia. WhatsApp: processo WhatsApp ou WhatsApp.* (o app atual é WhatsApp.Root). Outros: nada.
    /// </summary>
    public static bool Matches(SourceKind kind, string mediaTitle, string windowTitle, string processName) => kind switch
    {
        SourceKind.YouTubeMusic => windowTitle.Contains("YouTube Music", StringComparison.OrdinalIgnoreCase),
        SourceKind.ChromeTab => !string.IsNullOrWhiteSpace(mediaTitle)
            && processName.Equals("chrome", StringComparison.OrdinalIgnoreCase)
            && windowTitle.Contains(mediaTitle, StringComparison.OrdinalIgnoreCase),
        SourceKind.WhatsApp => processName.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase)
            || processName.StartsWith("WhatsApp.", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    /// <summary>Devolve false se não achou janela.</summary>
    public static bool Activate(MediaSnapshot media)
    {
        if (media.Kind == SourceKind.Other) return false;
        var hwnd = Find(media); // rápido: a aba já é a ativa, ou é o app do YT Music, ou WhatsApp
        if (hwnd == IntPtr.Zero && media.Kind is SourceKind.ChromeTab or SourceKind.YouTubeMusic)
            hwnd = ChromeTabs.TrySelect(ChromeTabs.NeedleFor(media.Kind, media.Title ?? ""));
        if (hwnd == IntPtr.Zero)
        {
            Log.Info($"Abrir fonte: nenhuma janela de {media.Kind} encontrada");
            return false;
        }
        if (IsIconic(hwnd)) ShowWindow(hwnd, SW_RESTORE);
        GetWindowThreadProcessId(hwnd, out uint pid);
        AllowSetForegroundWindow(pid);
        if (!SetForegroundWindow(hwnd) && !ForceForeground(hwnd))
            Log.Info("Abrir fonte: o Windows recusou trazer a janela para a frente");
        return true;
    }

    /// <summary>
    /// O Windows recusa SetForegroundWindow quando quem pede não é o dono do foco (janela aberta em
    /// segundo plano, sem estar minimizada: o ícone só pisca). Ligar a fila de entrada da nossa thread
    /// à da janela que está na frente faz o pedido valer.
    /// </summary>
    private static bool ForceForeground(IntPtr hwnd)
    {
        var front = GetForegroundWindow();
        uint frontThread = front == IntPtr.Zero ? 0 : GetWindowThreadProcessId(front, out _);
        uint ours = GetCurrentThreadId();
        bool attached = frontThread != 0 && frontThread != ours && AttachThreadInput(ours, frontThread, true);
        try
        {
            BringWindowToTop(hwnd);
            return SetForegroundWindow(hwnd);
        }
        finally
        {
            if (attached) AttachThreadInput(ours, frontThread, false);
        }
    }

    private static IntPtr Find(MediaSnapshot media)
    {
        var names = new Dictionary<uint, string>();
        IntPtr found = IntPtr.Zero;
        var title = new StringBuilder(512);
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            int len = GetWindowTextLength(h);
            if (len <= 0) return true;
            title.Clear();
            title.EnsureCapacity(len + 1);
            if (GetWindowText(h, title, title.Capacity) <= 0) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (!names.TryGetValue(pid, out var name))
            {
                try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; }
                catch (Exception) { name = ""; }
                names[pid] = name;
            }
            if (!Matches(media.Kind, media.Title ?? "", title.ToString(), name)) return true;
            found = h;
            return false; // ordem Z: a primeira é a mais à frente
        }, IntPtr.Zero);
        return found;
    }

    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr h, IntPtr l);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(uint pid);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
