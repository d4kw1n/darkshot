namespace DarkShot.Core;

public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 2 * 1024 * 1024;

    public static string FilePath
    {
        get
        {
            try { return AppPaths.LogFile; }
            catch { return Path.Combine(Path.GetTempPath(), "darkshot.log"); }
        }
    }

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message) => Write("ERROR", message, null);
    public static void Error(Exception ex, string message) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                var path = FilePath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                RollIfNeeded(path);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
                if (ex != null) line += Environment.NewLine + ex;
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    private static void RollIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < MaxBytes) return;
            var old = path + ".1";
            File.Move(path, old, overwrite: true);
        }
        catch { /* ignore */ }
    }
}
