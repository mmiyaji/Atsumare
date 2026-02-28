using System;
using System.IO;
using System.Text;

namespace Atsumare;

internal static class CrashLog
{
    private static readonly object _gate = new();
    private static string LogPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Atsumare");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "crash.log");
        }
    }

    public static void Write(string message)
    {
        try
        {
            lock (_gate)
            {
                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\r\n",
                    Encoding.UTF8);
            }
        }
        catch { }
    }

    public static void Write(Exception ex, string tag)
        => Write($"[{tag}] {ex.GetType().FullName}: {ex.Message}\r\n{ex}");
}