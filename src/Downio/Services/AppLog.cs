using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Downio.Services;

public static class AppLog
{
    private static readonly object Sync = new();
    private static string? _logFilePath;

    public static string LogFilePath
    {
        get
        {
            lock (Sync)
            {
                return _logFilePath ??= InitializeLogPath();
            }
        }
    }

    public static void Info(string message) => Write("INFO", message, null);

    public static void Warn(string message) => Write("WARN", message, null);

    public static void Error(string message) => Write("ERROR", message, null);

    public static void Error(Exception ex, string message) => Write("ERROR", message, ex);

    private static string InitializeLogPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "Downio", "logs");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "app.log");
    }

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Sync)
            {
                RotateIfNeeded(LogFilePath);

                var sb = new StringBuilder();
                sb.Append('[')
                  .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture))
                  .Append("] [")
                  .Append(level)
                  .Append("] ")
                  .Append(message);

                if (ex != null)
                {
                    sb.AppendLine();
                    sb.Append(ex);
                }

                sb.AppendLine();
                File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
        }
    }

    private static void RotateIfNeeded(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) return;

            const long maxBytes = 5 * 1024 * 1024;
            if (info.Length <= maxBytes) return;

            var backup = path + ".1";
            if (File.Exists(backup))
            {
                File.Delete(backup);
            }

            File.Move(path, backup);
        }
        catch
        {
        }
    }
}
