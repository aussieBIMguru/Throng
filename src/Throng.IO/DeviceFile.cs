using System.Text;
using System.Text.Json;
using Throng.Core.Common;
using Throng.Core.Library;
using Throng.Core.Serialization;

namespace Throng.IO;

/// <summary>
/// Reads and writes <c>.throngdevice</c> files (round 017, D-290): plain JSON of a <see cref="DeviceBundle"/> (one
/// device definition), UTF-8 without a byte order mark. Never throws.
/// </summary>
public static class DeviceFile
{
    /// <summary>File extension, with the dot.</summary>
    public const string Extension = ".throngdevice";

    /// <summary>Writes the bundle to <paramref name="path"/>, replacing any file there.</summary>
    public static Result Save(DeviceBundle bundle, string path)
    {
        try
        {
            string json = JsonSerializer.Serialize(bundle, JsonDefaults.Options);
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return Result.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Result.Fail($"The device couldn't be written to '{path}': {ex.Message}");
        }
    }

    /// <summary>Reads a bundle; a missing device, a newer format or bad JSON is a failure with a message.</summary>
    public static Result<DeviceBundle> Load(string path)
    {
        try
        {
            DeviceBundle? bundle = JsonSerializer.Deserialize<DeviceBundle>(File.ReadAllText(path), JsonDefaults.Options);
            if (bundle?.Device is null)
            {
                return Result<DeviceBundle>.Fail($"'{Path.GetFileName(path)}' doesn't hold a device.");
            }

            if (bundle.FormatVersion > DeviceBundle.CurrentFormatVersion)
            {
                return Result<DeviceBundle>.Fail($"'{Path.GetFileName(path)}' was saved by a newer version of Throng. Please update the app to import it.");
            }

            bundle.Device.ServiceTime ??= Distribution.Fixed(5);
            bundle.Device.TypeTag ??= string.Empty;
            bundle.Device.Name = string.IsNullOrWhiteSpace(bundle.Device.Name) ? "Imported device" : bundle.Device.Name;
            return Result<DeviceBundle>.Ok(bundle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or JsonException)
        {
            return Result<DeviceBundle>.Fail($"'{Path.GetFileName(path)}' couldn't be read as a device: {ex.Message}");
        }
    }
}
