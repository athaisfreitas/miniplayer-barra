using System.Diagnostics;
using System.Windows.Automation;
using MiniplayerBarra.Core;

namespace MiniplayerBarra.Ui;

/// <summary>Acha e seleciona, pela UI Automation, a aba do Chrome que está tocando.</summary>
internal static class ChromeTabs
{
    /// <summary>Sufixos que o Chrome põe no nome da aba que toca (inglês e português).</summary>
    private static readonly string[] AudioMarkers = { "Audio playing", "Reproduzindo áudio", "Áudio em reprodução" };

    /// <summary>YT Music: aba com "YouTube Music" no nome. Aba comum: o título da mídia.</summary>
    public static string NeedleFor(SourceKind kind, string mediaTitle) =>
        kind == SourceKind.YouTubeMusic ? "YouTube Music" : mediaTitle;

    /// <summary>
    /// Índice da aba a abrir: 1) abas cujo nome contém <paramref name="needle"/>, preferindo a que toca;
    /// 2) se nenhuma, a única aba que toca; 3) senão null.
    /// </summary>
    public static int? Pick(IReadOnlyList<string> tabNames, string needle)
    {
        int? firstByTitle = null;
        int? playingByTitle = null;
        int? singlePlaying = null;
        int playingCount = 0;
        for (int i = 0; i < tabNames.Count; i++)
        {
            var name = tabNames[i];
            bool playing = IsPlaying(name);
            if (playing) { playingCount++; singlePlaying = i; }
            if (string.IsNullOrWhiteSpace(needle) || !name.Contains(needle, StringComparison.OrdinalIgnoreCase)) continue;
            firstByTitle ??= i;
            if (playing) playingByTitle ??= i;
        }
        if (firstByTitle is not null) return playingByTitle ?? firstByTitle;
        return playingCount == 1 ? singlePlaying : null;
    }

    /// <summary>
    /// Seleciona a aba escolhida por <see cref="Pick"/> entre todas as janelas do Chrome.
    /// Devolve o HWND da janela da aba, ou IntPtr.Zero. Lento (centenas de ms): não chame na thread da interface.
    /// </summary>
    public static IntPtr TrySelect(string needle)
    {
        try
        {
            var windowCond = new PropertyCondition(AutomationElement.ClassNameProperty, "Chrome_WidgetWin_1");
            var tabCond = new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.TabItem),
                new PropertyCondition(AutomationElement.ClassNameProperty, "TabView"));

            var names = new List<string>();
            var found = new List<(AutomationElement Window, AutomationElement Tab)>();
            foreach (AutomationElement win in AutomationElement.RootElement.FindAll(TreeScope.Children, windowCond))
            {
                if (!IsChrome(win.Current.ProcessId)) continue; // VS Code/Electron usam a mesma classe
                foreach (AutomationElement tab in win.FindAll(TreeScope.Descendants, tabCond))
                {
                    names.Add(tab.Current.Name ?? "");
                    found.Add((win, tab));
                }
            }

            var index = Pick(names, needle);
            if (index is null)
            {
                Log.Info($"Abrir fonte: nenhuma aba do Chrome casa com \"{needle}\" ({names.Count} abas)");
                return IntPtr.Zero;
            }
            var (window, chosen) = found[index.Value];
            if (chosen.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var p))
                ((SelectionItemPattern)p).Select();
            return new IntPtr(window.Current.NativeWindowHandle);
        }
        catch (ElementNotAvailableException)
        {
            return IntPtr.Zero; // uma aba/janela fechou no meio da busca
        }
    }

    private static bool IsChrome(int pid)
    {
        try { using var p = Process.GetProcessById(pid); return p.ProcessName.Equals("chrome", StringComparison.OrdinalIgnoreCase); }
        catch (Exception) { return false; }
    }

    private static bool IsPlaying(string name)
    {
        foreach (var m in AudioMarkers)
            if (name.Contains(m, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
