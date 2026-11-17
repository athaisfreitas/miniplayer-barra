using System.Diagnostics;
using System.Drawing;
using System.Windows.Automation;
using MiniplayerBarra.Core;
using Microsoft.Win32;

namespace MiniplayerBarra.Ui;

/// <summary>
/// Lê onde estão as coisas na barra de tarefas: alinhamento e Widgets (registro), início e fim
/// dos ícones de apps e botão Widgets (UI Automation), e o limite à direita (Barra de Bateria ou
/// bandeja). A UIA é lenta: chame só ao reposicionar, nunca a cada quadro.
/// </summary>
internal static class TaskbarProbe
{
    private const string AdvancedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string RepeaterId = "TaskbarFrameRepeater";
    private const string StartId = "StartButton";
    private const string WidgetsId = "WidgetsButton";
    private const string BatteryProcess = "BarraBateria";
    private const int FallbackAppsHalf = 250;
    private const int FallbackWidgetsWidth = 160;

    private static bool _loggedUiaFailure;

    public static TaskbarInfo? Read(IntPtr trayHwnd)
    {
        if (trayHwnd == IntPtr.Zero || !Native.IsWindow(trayHwnd)) return null;
        if (!Native.GetWindowRect(trayHwnd, out var tr)) return null;
        var taskbar = Rectangle.FromLTRB(tr.Left, tr.Top, tr.Right, tr.Bottom);
        float scale = Native.GetDpiForWindow(trayHwnd) / 96f;
        if (scale <= 0) scale = 1;

        int? al = ReadDword("TaskbarAl");
        bool centered = al is null || al != 0;
        int? da = ReadDword("TaskbarDa");

        var uia = ReadIcons(trayHwnd);
        int appsLeft, appsRight;
        Rectangle? widgets = null;
        if (uia is { } u)
        {
            _loggedUiaFailure = false;
            appsLeft = u.AppsLeft;
            appsRight = u.AppsRight;
            if (da != 0 && u.Widgets is { IsEmpty: false } w) widgets = w;
        }
        else
        {
            if (!_loggedUiaFailure)
            {
                Log.Info("Barra de tarefas: UI Automation falhou; usando posições estimadas");
                _loggedUiaFailure = true;
            }
            int center = taskbar.Left + taskbar.Width / 2;
            int half = (int)(FallbackAppsHalf * scale);
            if (centered)
            {
                appsLeft = center - half;
                appsRight = center + half;
                if (da == 1) widgets = new Rectangle(taskbar.Left, taskbar.Top, (int)(FallbackWidgetsWidth * scale), taskbar.Height);
            }
            else
            {
                // Sem saber onde os ícones acabam, supõe que vão até o meio da barra.
                appsLeft = taskbar.Left;
                appsRight = center;
            }
        }

        int rightLimit = taskbar.Right;
        var notify = Native.FindWindowEx(trayHwnd, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notify != IntPtr.Zero && Native.GetWindowRect(notify, out var nr)) rightLimit = Math.Min(rightLimit, nr.Left);
        if (FindBatteryBar(trayHwnd) is { } bb) rightLimit = Math.Min(rightLimit, bb.Left);
        // Barra à esquerda com Widgets: o botão Widgets vai para a direita, perto da bandeja.
        if (!centered && widgets is { } wr && wr.Left >= appsRight) rightLimit = Math.Min(rightLimit, wr.Left);

        return new TaskbarInfo(taskbar, centered, widgets, appsLeft, appsRight, rightLimit);
    }

    private static int? ReadDword(string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AdvancedKey);
            return key?.GetValue(name) as int?;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private readonly record struct Icons(int AppsLeft, int AppsRight, Rectangle? Widgets);

    /// <summary>
    /// Shell_TrayWnd → … → TaskbarFrameRepeater → botões. Iniciar, apps, Pesquisa e Visão de
    /// Tarefas contam como ícones; o WidgetsButton também mora no repeater, mas fica de fora.
    /// </summary>
    private static Icons? ReadIcons(IntPtr trayHwnd)
    {
        try
        {
            var root = AutomationElement.FromHandle(trayHwnd);
            var walker = TreeWalker.RawViewWalker;
            var repeater = FindById(root, walker, RepeaterId, maxDepth: 5);
            if (repeater is null) return null;

            int left = int.MaxValue, right = int.MinValue;
            Rectangle? widgets = null;
            bool sawStart = false;
            for (var c = walker.GetFirstChild(repeater); c != null; c = walker.GetNextSibling(c))
            {
                var id = c.Current.AutomationId;
                var r = c.Current.BoundingRectangle;
                if (r.IsEmpty || r.Width <= 0) continue;
                var rect = Rectangle.FromLTRB((int)r.Left, (int)r.Top, (int)r.Right, (int)r.Bottom);
                if (id == WidgetsId)
                {
                    widgets = rect;
                    continue;
                }
                if (id == StartId) sawStart = true;
                left = Math.Min(left, rect.Left);
                right = Math.Max(right, rect.Right);
            }
            if (!sawStart || left == int.MaxValue) return null;
            return new Icons(left, right, widgets);
        }
        catch (Exception ex)
        {
            Log.Error("Barra de tarefas UIA", ex);
            return null;
        }
    }

    // Busca em largura pela árvore crua, limitada em profundidade (o repeater fica no nível 4).
    private static AutomationElement? FindById(AutomationElement root, TreeWalker walker, string id, int maxDepth)
    {
        var level = new List<AutomationElement> { root };
        for (int depth = 0; depth < maxDepth && level.Count > 0; depth++)
        {
            var next = new List<AutomationElement>();
            foreach (var e in level)
                for (var c = walker.GetFirstChild(e); c != null; c = walker.GetNextSibling(c))
                {
                    if (c.Current.AutomationId == id) return c;
                    next.Add(c);
                }
            level = next;
        }
        return null;
    }

    /// <summary>A janela do Barra de Bateria: filha direta e visível da Shell_TrayWnd do processo BarraBateria.</summary>
    private static Rectangle? FindBatteryBar(IntPtr trayHwnd)
    {
        var pids = new HashSet<uint>();
        foreach (var p in Process.GetProcessesByName(BatteryProcess))
            using (p) pids.Add((uint)p.Id);
        if (pids.Count == 0) return null;

        Rectangle? found = null;
        Native.EnumChildWindows(trayHwnd, (h, _) =>
        {
            if (Native.GetParent(h) != trayHwnd || !Native.IsWindowVisible(h)) return true;
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (!pids.Contains(pid) || !Native.GetWindowRect(h, out var r)) return true;
            var rect = Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (rect.Width <= 0) return true;
            if (found is null || rect.Left < found.Value.Left) found = rect;
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
