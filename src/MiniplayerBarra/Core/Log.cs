namespace MiniplayerBarra.Core;

/// <summary>Log mínimo: guarda as últimas mensagens em memória e, se configurado, num arquivo de até 1 MB.</summary>
public static class Log
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly List<string> RecentLines = new();

    public static string? FilePath { get; set; }

    public static IReadOnlyList<string> Recent
    {
        get { lock (Gate) return RecentLines.ToList(); }
    }

    public static void Error(string source, Exception ex) => Write($"ERRO [{source}] {ex.GetType().Name}: {ex.Message}");

    public static void Info(string message) => Write(message);

    private static void Write(string message)
    {
        var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss} {message}";
        lock (Gate)
        {
            RecentLines.Add(line);
            if (RecentLines.Count > 200) RecentLines.RemoveAt(0);
            if (FilePath is null) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes) File.Move(FilePath, FilePath + ".old", overwrite: true);
                File.AppendAllText(FilePath, line + Environment.NewLine);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
