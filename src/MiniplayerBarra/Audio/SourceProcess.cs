using System.Runtime.InteropServices;
using MiniplayerBarra.Core;

namespace MiniplayerBarra.Audio;

/// <summary>Qual processo produz o som de cada tipo de fonte, e a raiz da árvore desse processo.</summary>
public static class SourceProcess
{
    public static string? ProcessNameFor(SourceKind kind) => kind switch
    {
        SourceKind.YouTubeMusic or SourceKind.ChromeTab => "chrome",
        SourceKind.WhatsApp => "WhatsApp",
        _ => null,
    };

    /// <summary>
    /// Raiz = processo com esse nome cujo pai não tem o mesmo nome. O som do Chrome sai de um filho
    /// (serviço de áudio), por isso a captura usa a árvore inteira a partir da raiz.
    /// Se houver mais de uma raiz, fica a que tem mais descendentes com o mesmo nome.
    /// </summary>
    public static int? FindRootPid(string processName, IEnumerable<(int Pid, int ParentPid, string Name)> processes)
    {
        var matching = new Dictionary<int, int>();   // pid → pai, só dos processos com o nome pedido
        foreach (var p in processes)
            if (SameName(p.Name, processName)) matching[p.Pid] = p.ParentPid;
        if (matching.Count == 0) return null;

        int? best = null;
        int bestCount = -1;
        foreach (var (pid, parent) in matching)
        {
            if (matching.ContainsKey(parent) && parent != pid) continue;
            int count = matching.Keys.Count(other => other != pid && HasAncestor(other, pid, matching));
            if (count > bestCount) (best, bestCount) = (pid, count);
        }
        return best;
    }

    private static bool HasAncestor(int pid, int ancestor, Dictionary<int, int> parents)
    {
        for (int steps = 0; steps < parents.Count && parents.TryGetValue(pid, out var parent) && parent != pid; steps++)
        {
            if (parent == ancestor) return true;
            pid = parent;
        }
        return false;
    }

    /// <summary>"chrome" casa com chrome.exe; "WhatsApp" casa com WhatsApp.exe e WhatsApp.Root.exe (o app atual).</summary>
    private static bool SameName(string exeName, string processName)
    {
        var name = exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exeName[..^4] : exeName;
        return name.Equals(processName, StringComparison.OrdinalIgnoreCase)
            || (name.Length > processName.Length && name[processName.Length] == '.'
                && name.StartsWith(processName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Lista atual de processos (pid, pai, nome do .exe) via CreateToolhelp32Snapshot.</summary>
    public static List<(int Pid, int ParentPid, string Name)> Snapshot()
    {
        var list = new List<(int, int, string)>();
        IntPtr snap = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snap == InvalidHandle) return list;
        try
        {
            var e = new ProcessEntry32W { dwSize = (uint)Marshal.SizeOf<ProcessEntry32W>() };
            if (!Process32FirstW(snap, ref e)) return list;
            do list.Add(((int)e.th32ProcessID, (int)e.th32ParentProcessID, e.szExeFile));
            while (Process32NextW(snap, ref e));
        }
        finally { CloseHandle(snap); }
        return list;
    }

    private const uint Th32csSnapProcess = 0x2;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32W
    {
        public uint dwSize, cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32FirstW(IntPtr snap, ref ProcessEntry32W entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool Process32NextW(IntPtr snap, ref ProcessEntry32W entry);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
