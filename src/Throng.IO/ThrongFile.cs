using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Throng.Core.Common;
using Throng.Core.Library;
using Throng.Core.Model;
using Throng.Core.Serialization;

namespace Throng.IO;

/// <summary>
/// Reads and writes .throng study files: a zip containing manifest.json, study.json and library.json (§14.2).
/// Never throws to callers; failures come back as <see cref="Result"/> messages.
/// </summary>
public static class ThrongFile
{
    /// <summary>File extension including the dot.</summary>
    public const string Extension = ".throng";

    /// <summary>Format marker written into the manifest.</summary>
    public const string FormatName = "throng-study";

    /// <summary>
    /// Format version written by this build. Bump it and add a migration when the schema changes.
    /// v1: levels and regions (rounds 001–003). v2: adds <c>connectors</c> and <c>shafts</c> (round 004, D-064).
    /// v3: region spans and height modes, exclusions, per-level stairs, travelators, <c>devices</c> and
    /// <c>underlays</c> with their images under <c>underlays/</c> (round 005, D-086).
    /// v4: emission rules on spawn zones, keep side, staff-only connectors (round 006, D-099, D-105, D-110).
    /// v5: rate curves on emission rules (round 007, D-129).
    /// v6: mix lines (profile + journey + share + spawn tags) on emission rules, device queue settings and journeys
    /// in the library (round 009, D-151, D-153, D-157).
    /// </summary>
    public const int CurrentFormatVersion = 6;

    /// <summary>Zip folder holding underlay images (§14.2, D-083).</summary>
    public const string UnderlayFolder = "underlays/";

    private const string ManifestEntry = "manifest.json";
    private const string StudyEntry = "study.json";
    private const string LibraryEntry = "library.json";

    /// <summary>
    /// Saves a study. Writes to a temporary file first and then swaps it into place, so a crash mid-save
    /// never corrupts the existing file.
    /// </summary>
    /// <param name="study">Study to save.</param>
    /// <param name="path">Destination path.</param>
    /// <param name="createdUtc">Original creation time to preserve; now when null.</param>
    public static Result Save(Study study, string path, DateTime? createdUtc = null)
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
                AppVersion = GetAppVersion(),
                CreatedUtc = createdUtc ?? now,
                ModifiedUtc = now,
                Seed = study.Settings.Seed,
            };

            using (FileStream stream = File.Create(tempPath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                WriteEntry(zip, ManifestEntry, manifest);
                WriteEntry(zip, StudyEntry, study);
                WriteEntry(zip, LibraryEntry, study.Library);
                foreach (Underlay underlay in study.Underlays)
                {
                    if (study.UnderlayImages.TryGetValue(underlay.Id, out byte[]? bytes) && IsUnderlayEntry(underlay.ImageEntry))
                    {
                        ZipArchiveEntry entry = zip.CreateEntry(underlay.ImageEntry, CompressionLevel.NoCompression);
                        using Stream entryStream = entry.Open();
                        entryStream.Write(bytes, 0, bytes.Length);
                    }
                }
            }

            File.Move(tempPath, path, overwrite: true);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            TryDelete(tempPath);
            return Result.Fail($"Couldn't save '{Path.GetFileName(path)}': {ex.Message}");
        }
    }

    /// <summary>Loads a study, migrating older formats where possible.</summary>
    public static Result<Study> Load(string path)
    {
        if (!File.Exists(path))
        {
            return Result<Study>.Fail($"File not found: {path}");
        }

        try
        {
            using FileStream stream = File.OpenRead(path);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

            ThrongManifest? manifest = ReadEntry<ThrongManifest>(zip, ManifestEntry);
            if (manifest is null || !string.Equals(manifest.Format, FormatName, StringComparison.Ordinal))
            {
                return Result<Study>.Fail($"'{Path.GetFileName(path)}' isn't a Throng study.");
            }

            if (manifest.FormatVersion > CurrentFormatVersion)
            {
                return Result<Study>.Fail(
                    $"'{Path.GetFileName(path)}' was saved by a newer version of Throng (file format {manifest.FormatVersion}; this version reads up to {CurrentFormatVersion}). Please update the app to open it.");
            }

            Study? study = ReadEntry<Study>(zip, StudyEntry);
            if (study is null)
            {
                return Result<Study>.Fail($"'{Path.GetFileName(path)}' has no study data.");
            }

            study.Library = ReadEntry<TemplateLibrary>(zip, LibraryEntry) ?? new TemplateLibrary();
            FormatMigrations.Apply(study, manifest.FormatVersion);
            study.UnderlayImages = new Dictionary<Guid, byte[]>();
            foreach (Underlay underlay in study.Underlays)
            {
                // A missing image leaves the underlay in place (CHECK warns); it draws as an outline.
                if (IsUnderlayEntry(underlay.ImageEntry) && zip.GetEntry(underlay.ImageEntry) is { } entry)
                {
                    using Stream entryStream = entry.Open();
                    using var buffer = new MemoryStream();
                    entryStream.CopyTo(buffer);
                    study.UnderlayImages[underlay.Id] = buffer.ToArray();
                }
            }
            return Result<Study>.Ok(study);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException)
        {
            return Result<Study>.Fail($"Couldn't open '{Path.GetFileName(path)}': {ex.Message}");
        }
    }

    /// <summary>Reads just the manifest, for created-date preservation and quick inspection.</summary>
    public static Result<ThrongManifest> ReadManifest(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            ThrongManifest? manifest = ReadEntry<ThrongManifest>(zip, ManifestEntry);
            return manifest is null ? Result<ThrongManifest>.Fail("No manifest.") : Result<ThrongManifest>.Ok(manifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException)
        {
            return Result<ThrongManifest>.Fail(ex.Message);
        }
    }

    /// <summary>Zip entry name for a new underlay image, keeping the original extension.</summary>
    public static string UnderlayEntryName(Guid underlayId, string originalFileName)
    {
        string extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (extension is not (".png" or ".jpg" or ".jpeg"))
        {
            extension = ".png";
        }

        return $"{UnderlayFolder}{underlayId:N}{extension}";
    }

    /// <summary>Only plain names inside the underlays folder are read or written (no path tricks from a hand-edited file).</summary>
    private static bool IsUnderlayEntry(string name) =>
        name.StartsWith(UnderlayFolder, StringComparison.Ordinal) && name.IndexOf("..", StringComparison.Ordinal) < 0
        && name.IndexOf('/', UnderlayFolder.Length) < 0 && name.Length > UnderlayFolder.Length;

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
        Assembly assembly = Assembly.GetEntryAssembly() ?? typeof(ThrongFile).Assembly;
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
        catch (IOException)
        {
            // Best effort: a leftover .tmp file is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }
}
