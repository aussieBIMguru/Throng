using System.Text;
using System.Text.Json;
using Throng.Core.Common;
using Throng.Core.Library;
using Throng.Core.Serialization;

namespace Throng.IO;

/// <summary>
/// Reads and writes <c>.throngprofile</c> files (BRIEF §8.6, round 016, D-275): plain JSON of a
/// <see cref="ProfileBundle"/> (one profile and its journeys), UTF-8 without a byte order mark. Never throws.
/// </summary>
public static class ProfileFile
{
    /// <summary>File extension, with the dot.</summary>
    public const string Extension = ".throngprofile";

    /// <summary>Writes the bundle to <paramref name="path"/>, replacing any file there.</summary>
    public static Result Save(ProfileBundle bundle, string path)
    {
        try
        {
            string json = JsonSerializer.Serialize(bundle, JsonDefaults.Options);
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Result.Fail($"The profile couldn't be written to '{path}': {ex.Message}");
        }
    }

    /// <summary>Reads a bundle; a missing profile, a newer format or bad JSON is a failure with a message.</summary>
    public static Result<ProfileBundle> Load(string path)
    {
        try
        {
            ProfileBundle? bundle = JsonSerializer.Deserialize<ProfileBundle>(File.ReadAllText(path), JsonDefaults.Options);
            if (bundle?.Profile is null)
            {
                return Result<ProfileBundle>.Fail($"'{Path.GetFileName(path)}' doesn't hold a profile.");
            }

            if (bundle.FormatVersion > ProfileBundle.CurrentFormatVersion)
            {
                return Result<ProfileBundle>.Fail($"'{Path.GetFileName(path)}' was saved by a newer version of Throng. Please update the app to import it.");
            }

            bundle.Journeys ??= [];
            bundle.Journeys.RemoveAll(j => j is null);
            foreach (JourneyTemplate journey in bundle.Journeys)
            {
                journey.Steps ??= [];
                journey.Steps.RemoveAll(s => s is null);
            }

            bundle.Profile.DevicePreferences ??= [];
            bundle.Profile.DevicePreferences.RemoveAll(p => p is null);
            return Result<ProfileBundle>.Ok(bundle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException)
        {
            return Result<ProfileBundle>.Fail($"'{Path.GetFileName(path)}' couldn't be read as a profile: {ex.Message}");
        }
    }
}
