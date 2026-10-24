using System.Globalization;
using System.IO;

namespace Throng.App.Services;

/// <summary>
/// Minimal append-only text log in %LocalAppData%\Throng\Logs. Logging never throws; if the log can't be written
/// the message is dropped.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly object Gate = new();

    /// <summary>Creates the log folder and rolls the log when it grows large.</summary>
    public static void Initialise()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogFolder);
            var info = new FileInfo(AppPaths.LogFile);
            if (info.Exists && info.Length > MaxBytes)
            {
                File.Move(AppPaths.LogFile, Path.ChangeExtension(AppPaths.LogFile, ".old.log"), overwrite: true);
            }
        }
        catch (IOException)
        {
            // Logging is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Logging is best effort.
        }
    }

    /// <summary>Writes an informational line.</summary>
    public static void Info(string message) => Write("INFO", message, null);

    /// <summary>Writes a warning line.</summary>
    public static void Warn(string message) => Write("WARN", message, null);

    /// <summary>Writes an error line, with the exception if there is one.</summary>
    public static void Error(string message, Exception? exception) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception? exception)
    {
        string line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} [{level}] {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (Gate)
        {
            try
            {
                File.AppendAllText(AppPaths.LogFile, line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Logging is best effort.
            }
            catch (UnauthorizedAccessException)
            {
                // Logging is best effort.
            }
        }
    }
}
