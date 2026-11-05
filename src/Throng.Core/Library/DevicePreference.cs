namespace Throng.Core.Library;

/// <summary>
/// A profile's liking for a device preset or type (D-166): <see cref="BonusS"/> seconds come off that device's cost
/// when the person chooses which device of a journey step to use. Wheelchair users favouring the accessible gate is
/// the built-in example.
/// </summary>
/// <remarks>Immutable: edits replace the entry (and the profile's list) so undo restores the exact value.</remarks>
public sealed record DevicePreference
{
    /// <summary>
    /// Device preset key (e.g. "device.gate-accessible") or type tag (e.g. "Gate"), matched case-insensitively
    /// against the placed device's preset key and type tag.
    /// </summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>Seconds taken off the device's choice cost (negative values make it less attractive).</summary>
    public double BonusS { get; init; }

    /// <summary>True when this preference applies to a device with this preset key and type tag.</summary>
    public bool Matches(string? presetKey, string? typeTag) =>
        !string.IsNullOrWhiteSpace(Target)
        && (string.Equals(Target.Trim(), presetKey, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Target.Trim(), typeTag, StringComparison.OrdinalIgnoreCase));
}
