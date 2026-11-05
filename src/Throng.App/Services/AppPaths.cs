using System.IO;

namespace Throng.App.Services;

/// <summary>Well-known per-user folders.</summary>
public static class AppPaths
{
    /// <summary>Roaming user library of custom templates (§22.5).</summary>
    public static string UserLibraryFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Throng", "Library");

    /// <summary>Local log folder.</summary>
    public static string LogFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Throng", "Logs");

    /// <summary>Current log file.</summary>
    public static string LogFile { get; } = Path.Combine(LogFolder, "throng.log");
}
