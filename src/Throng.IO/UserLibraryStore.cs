using System.Globalization;
using System.Text;
using System.Text.Json;
using Throng.Core.Common;
using Throng.Core.Library;
using Throng.Core.Serialization;

namespace Throng.IO;

/// <summary>
/// Your library on disk (round 020, D-025, D-341, D-348): <c>library.json</c> in one folder (the app passes
/// <c>%AppData%\Throng\Library</c>; tests pass a temp folder, so they never touch the real one). Every write first copies
/// the current file to <c>library.bak.json</c>, then writes a temp file and swaps it in. A file that can't be read is
/// moved aside (<c>library.unreadable-yyyyMMdd-HHmmss.json</c>) and the library starts empty; a file from a newer
/// version of Throng is left alone and the library is read-only for the session. Never throws.
/// </summary>
public sealed class UserLibraryStore
{
    /// <summary>Library file name.</summary>
    public const string FileName = "library.json";

    /// <summary>Backup of the previous library file (D-348).</summary>
    public const string BackupFileName = "library.bak.json";

    /// <summary>Creates a store over <paramref name="folder"/> (created on the first save). Call <see cref="Load"/> next.</summary>
    public UserLibraryStore(string folder)
    {
        Folder = folder;
    }

    /// <summary>The library folder.</summary>
    public string Folder { get; }

    /// <summary>Full path of <c>library.json</c>.</summary>
    public string FilePath => Path.Combine(Folder, FileName);

    /// <summary>Full path of <c>library.bak.json</c>.</summary>
    public string BackupPath => Path.Combine(Folder, BackupFileName);

    /// <summary>Your library as loaded (empty until <see cref="Load"/>, or when the file couldn't be read).</summary>
    public TemplateLibrary Library { get; private set; } = new();

    /// <summary>True when the file was written by a newer Throng: nothing is saved over it this session.</summary>
    public bool IsReadOnly { get; private set; }

    /// <summary>
    /// Reads <c>library.json</c>. A missing file is an empty library (success). An unreadable file is moved aside and
    /// the library starts empty; a newer format keeps the file and makes the store read-only. Both of those return a
    /// failure with a message to log and show, but leave a usable (empty) <see cref="Library"/>.
    /// </summary>
    public Result Load()
    {
        Library = new TemplateLibrary();
        IsReadOnly = false;
        if (!File.Exists(FilePath))
        {
            return Result.Ok();
        }

        TemplateLibrary? loaded;
        try
        {
            loaded = JsonSerializer.Deserialize<TemplateLibrary>(File.ReadAllText(FilePath), JsonDefaults.Options);
        }
        catch (JsonException ex)
        {
            return SetAside($"Your library file couldn't be read ({ex.Message}).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // Can't read it at all (locked, no access): leave it where it is and don't save over it.
            IsReadOnly = true;
            return Result.Fail($"Your library couldn't be opened ({ex.Message}); it's empty for this session and won't be saved.");
        }

        if (loaded is null)
        {
            return SetAside("Your library file was empty or not a library.");
        }

        if (loaded.FormatVersion > TemplateLibrary.CurrentFormatVersion)
        {
            IsReadOnly = true;
            return Result.Fail("Your library was saved by a newer version of Throng; it's empty for this session and won't be changed. Update the app to use it.");
        }

        Library = Normalise(loaded);
        return Result.Ok();
    }

    /// <summary>
    /// Writes <see cref="Library"/> to <c>library.json</c>, copying the previous file to <c>library.bak.json</c> first
    /// (D-348). Fails (without writing) while <see cref="IsReadOnly"/>.
    /// </summary>
    public Result Save()
    {
        if (IsReadOnly)
        {
            return Result.Fail("Your library is read-only this session (see the log), so the change wasn't saved.");
        }

        string tempPath = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Folder);
            if (File.Exists(FilePath))
            {
                File.Copy(FilePath, BackupPath, overwrite: true);
            }

            Library.FormatVersion = TemplateLibrary.CurrentFormatVersion;
            string json = JsonSerializer.Serialize(Library, JsonDefaults.Options);
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, FilePath, overwrite: true);
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or JsonException)
        {
            TryDelete(tempPath);
            return Result.Fail($"Your library couldn't be saved: {ex.Message}");
        }
    }

    /// <summary>
    /// Changes your library and saves it as one step (D-348): <paramref name="change"/> edits a copy, which replaces
    /// <see cref="Library"/> only when it saves; on failure the library is left as it was. No undo: the backup file is
    /// the way back.
    /// </summary>
    public Result Update(Action<TemplateLibrary> change)
    {
        if (IsReadOnly)
        {
            return Result.Fail("Your library is read-only this session (see the log), so the change wasn't saved.");
        }

        TemplateLibrary previous = Library;
        TemplateLibrary working;
        try
        {
            working = JsonSerializer.Deserialize<TemplateLibrary>(JsonSerializer.Serialize(previous, JsonDefaults.Options), JsonDefaults.Options) ?? new TemplateLibrary();
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return Result.Fail($"Your library couldn't be changed: {ex.Message}");
        }

        change(Normalise(working));
        Library = working;
        Result saved = Save();
        if (!saved.IsSuccess)
        {
            Library = previous;
        }

        return saved;
    }

    /// <summary>Null lists made empty, null entries dropped, journeys as graphs (legacy step lists converted).</summary>
    public static TemplateLibrary Normalise(TemplateLibrary library)
    {
        library.AgentProfiles ??= [];
        library.Devices ??= [];
        library.Connectors ??= [];
        library.Lifts ??= [];
        library.Schedules ??= [];
        library.Journeys ??= [];
        library.AgentProfiles.RemoveAll(t => t is null);
        library.Devices.RemoveAll(t => t is null);
        library.Connectors.RemoveAll(t => t is null);
        library.Lifts.RemoveAll(t => t is null);
        library.Schedules.RemoveAll(t => t is null);
        library.Journeys.RemoveAll(t => t is null);
        foreach (AgentProfileTemplate profile in library.AgentProfiles)
        {
            profile.DevicePreferences ??= [];
            profile.DevicePreferences.RemoveAll(p => p is null);
        }

        foreach (JourneyTemplate journey in library.Journeys)
        {
            journey.Steps?.RemoveAll(s => s is null);
            JourneyGraph.Ensure(journey);
        }

        return library;
    }

    /// <summary>Moves the unreadable file aside (kept for the user) and starts empty.</summary>
    private Result SetAside(string problem)
    {
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        string aside = Path.Combine(Folder, $"library.unreadable-{stamp}.json");
        try
        {
            File.Move(FilePath, aside, overwrite: true);
            return Result.Fail($"{problem} It was kept as '{Path.GetFileName(aside)}' and your library starts empty.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            // Couldn't move it: don't save over it either.
            IsReadOnly = true;
            return Result.Fail($"{problem} It couldn't be moved aside ({ex.Message}), so your library is empty and read-only this session.");
        }
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
