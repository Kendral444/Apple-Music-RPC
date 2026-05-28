namespace AppleMusicRPC;

/// <summary>Logger minimal vers fichier — utilisé uniquement pour le debug.</summary>
internal static class Log
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppleMusicRPC", "debug.log");

    private static readonly object _lock = new();

    static Log()
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            // Rotation : on garde seulement les 500 dernières lignes
            if (System.IO.File.Exists(Path))
            {
                var lines = System.IO.File.ReadAllLines(Path);
                if (lines.Length > 500)
                    System.IO.File.WriteAllLines(Path, lines[^400..]);
            }
        }
        catch { }
    }

    public static void Info(string msg)  => Write("INFO ", msg);
    public static void Warn(string msg)  => Write("WARN ", msg);
    public static void Error(string msg) => Write("ERROR", msg);
    public static void Error(string msg, Exception ex) => Write("ERROR", $"{msg} — {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");

    private static void Write(string level, string msg)
    {
        var line = $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {msg}";
        lock (_lock)
        {
            try { System.IO.File.AppendAllText(Path, line + Environment.NewLine); }
            catch { }
        }
        System.Diagnostics.Debug.WriteLine(line);
    }
}
