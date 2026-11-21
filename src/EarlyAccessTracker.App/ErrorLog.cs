using EarlyAccessTracker.Core;

namespace EarlyAccessTracker.App;

/// <summary>Minimal append-only log for crashes; a WinExe has no console to see them otherwise.</summary>
internal static class ErrorLog
{
    private static readonly Lock Sync = new();

    public static void Write(string context, Exception ex)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                File.AppendAllText(AppPaths.ErrorLogPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never take the app down.
        }
    }
}
