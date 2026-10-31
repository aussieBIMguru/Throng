using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Throng.Core.Common;
using Throng.Core.Library;
using Throng.Core.Serialization;

namespace Throng.IO;

/// <summary>
/// Reads and writes <c>.thronglib</c> files (BRIEF §708, round 020, D-347): a zip of JSON in the same style as a
/// <c>.throng</c> study, holding <c>manifest.json</c> (format marker and version) and <c>library.json</c> (a
/// <see cref="TemplateLibrary"/> with the exported items). Never throws.
/// </summary>
public static class LibraryFile
{
    /// <summary>File extension, with the dot.</summary>
    public const string Extension = ".thronglib";

    /// <summary>Format marker written into the manifest.</summary>
    public const string FormatName = "throng-library";

    /// <summary>Format version written by this build (1: profiles, devices and journeys as graphs; round 020).</summary>
    public const int CurrentFormatVersion = 1;

    private const string ManifestEntry = "manifest.json";
    private const string LibraryEntry = "library.json";

    /// <summary>Writes <paramref name="library"/> to <paramref name="path"/> (temp file, then swapped in).</summary>
    public static Result Save(TemplateLibrary library, string path)
    {
        string tempPath = path + ".tmp";
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            DateTime now = DateTime.UtcNow;
            var manifest = new ThrongManifest
            {
                Format = FormatName,
                FormatVersion = CurrentFormatVersion,
                AppVersion = GetAppVersion(),
                CreatedUtc = now,
                ModifiedUtc = now,
            };

            using (FileStream stream = File.Create(tempPath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(zip, ManifestEntry, manifest);
                WriteEntry(zip, LibraryEntry, library);
            }

            File.Move(tempPath, path, overwrite: true);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or ArgumentException)
        {
            TryDelete(tempPath);
            return Result.Fail($"Couldn't write '{Path.GetFileName(path)}': {ex.Message}");
        }
    }

    /// <summary>Reads a <c>.thronglib</c>; not a library, a newer format or bad data is a failure with a message.</summary>
    public static Result<TemplateLibrary> Load(string path)
    {
        if (!File.Exists(path))
        {
            return Result<TemplateLibrary>.Fail($"File not found: {path}");
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            ThrongManifest? manifest = ReadEntry<ThrongManifest>(zip, ManifestEntry);
            if (manifest is null || !string.Equals(manifest.Format, FormatName, StringComparison.Ordinal))
            {
                return Result<TemplateLibrary>.Fail($"'{Path.GetFileName(path)}' isn't a Throng library.");
            }

            if (manifest.FormatVersion > CurrentFormatVersion)
            {
                return Result<TemplateLibrary>.Fail($"'{Path.GetFileName(path)}' was saved by a newer version of Throng. Please update the app to import it.");
            }

            TemplateLibrary? library = ReadEntry<TemplateLibrary>(zip, LibraryEntry);
            if (library is null)
            {
                return Result<TemplateLibrary>.Fail($"'{Path.GetFileName(path)}' holds no library.");
            }

            return Result<TemplateLibrary>.Ok(UserLibraryStore.Normalise(library));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException or ArgumentException)
        {
            return Result<TemplateLibrary>.Fail($"Couldn't read '{Path.GetFileName(path)}': {ex.Message}");
        }
    }

    private static void WriteEntry<T>(ZipArchive zip, string name, T value)
    {
        ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using Stream entryStream = entry.Open();
        JsonSerializer.Serialize(entryStream, value, JsonDefaults.Options);
    }

    private static T? ReadEntry<T>(ZipArchive zip, string name)
        where T : class
    {
        ZipArchiveEntry? entry = zip.GetEntry(name);
        if (entry is null)
        {
            return null;
        }

        using Stream entryStream = entry.Open();
        return JsonSerializer.Deserialize<T>(entryStream, JsonDefaults.Options);
    }

    private static string GetAppVersion()
    {
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(LibraryFile).Assembly;
        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover .tmp file is harmless.
        }
    }
}
