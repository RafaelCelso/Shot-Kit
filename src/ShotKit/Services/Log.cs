using System.IO;

namespace ShotKit.Services;

/// <summary>Log local simples em %LOCALAPPDATA%\ShotKit\logs. Nada é enviado para fora da máquina.</summary>
public static class Log
{
    private static readonly object Gate = new();
    private const long MaxBytes = 1024 * 1024;

    private static string FilePath => Path.Combine(AppSettings.DataFolder, "logs", "shotkit.log");

    public static void Info(string message) => Write("INFO", message, null);

    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".1", overwrite: true);

                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{(ex != null ? Environment.NewLine + ex : "")}{Environment.NewLine}");
            }
        }
        catch
        {
            // Log nunca deve derrubar o app.
        }
    }
}
