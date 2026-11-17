using Microsoft.Win32;

namespace MiniplayerBarra;

/// <summary>Abre o app junto com o Windows (chave Run do usuário, sem administrador).</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "MiniplayerBarra";

    /// <summary>Só registra quando o .exe está na pasta de instalação (evita registrar builds de desenvolvimento).</summary>
    public static bool ShouldRegister(string exePath, string localAppData)
    {
        if (string.IsNullOrWhiteSpace(exePath) || string.IsNullOrWhiteSpace(localAppData)) return false;
        try
        {
            var installDir = Path.GetFullPath(Path.Combine(localAppData, "Programs", "MiniplayerBarra"))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var exeDir = Path.GetDirectoryName(Path.GetFullPath(exePath));
            if (exeDir is null) return false;
            exeDir = exeDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(exeDir, installDir, StringComparison.OrdinalIgnoreCase)
                && string.Equals(Path.GetFileName(exePath), "MiniplayerBarra.exe", StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    /// <summary>Grava/atualiza o caminho do .exe (o caminho pode mudar entre instalações).</summary>
    public static void Ensure(string exePath)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        var value = $"\"{exePath}\"";
        if (!Equals(key.GetValue(ValueName), value)) key.SetValue(ValueName, value);
    }
}
