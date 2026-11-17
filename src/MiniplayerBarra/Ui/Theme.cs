using System.Drawing;
using Microsoft.Win32;

namespace MiniplayerBarra.Ui;

/// <summary>Cores da barra conforme o tema da barra de tarefas (claro/escuro) e a cor de destaque do Windows.</summary>
public record Theme(Color Text, Color Accent)
{
    private static readonly Color DefaultAccent = Color.FromArgb(0x00, 0x78, 0xD4);

    /// <summary>SystemUsesLightTheme: 0/null = barra escura (texto branco), 1 = barra clara (texto preto).</summary>
    public static Theme FromRegistryValue(int? systemUsesLightTheme, Color? accent = null) =>
        new(systemUsesLightTheme == 1 ? Color.Black : Color.White, accent ?? DefaultAccent);

    /// <summary>AccentColor do DWM é um DWORD em ABGR (0xAABBGGRR); o alfa é ignorado.</summary>
    public static Color AccentFromAbgr(int abgr) =>
        Color.FromArgb(abgr & 0xFF, (abgr >> 8) & 0xFF, (abgr >> 16) & 0xFF);

    public static Theme Current()
    {
        int? light = null;
        Color accent = DefaultAccent;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            light = key?.GetValue("SystemUsesLightTheme") as int?;
        }
        catch (Exception) { }
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (key?.GetValue("AccentColor") is int abgr) accent = AccentFromAbgr(abgr);
        }
        catch (Exception) { }
        return FromRegistryValue(light, accent);
    }
}
