namespace Throng.Core.Library;

/// <summary>
/// What a <c>.throngdevice</c> file holds (round 017, D-290): one device definition, so a device type made in one
/// study can be placed in another.
/// </summary>
public sealed class DeviceBundle
{
    /// <summary>Current bundle format version.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Format version of this file.</summary>
    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>The device definition.</summary>
    public DeviceTemplate Device { get; set; } = new();
}
